﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

// Very big thanks to ILspy here.
// 

namespace DLLdotNetResourceReader
{
	internal class MyResourceReader : IEnumerable<KeyValuePair<string, object>>, IDisposable
	{

		sealed class MyBinaryReader : BinaryReader
		{
			public MyBinaryReader(Stream input, bool leaveOpen) : base(input, Encoding.UTF8, leaveOpen)
			{
			}

			// upgrade from protected to public visibility
			public new int Read7BitEncodedInt()
			{
				return base.Read7BitEncodedInt();
			}

			public void Seek(long pos, SeekOrigin origin)
			{
				BaseStream.Seek(pos, origin);
			}
		}

		enum ResourceTypeCode
		{
			Null = 0,
			String = 1,
			Boolean = 2,
			Char = 3,
			Byte = 4,
			SByte = 5,
			Int16 = 6,
			UInt16 = 7,
			Int32 = 8,
			UInt32 = 9,
			Int64 = 10,
			UInt64 = 11,
			Single = 12,
			Double = 13,
			Decimal = 14,
			DateTime = 0xF,
			TimeSpan = 0x10,
			LastPrimitive = 0x10,
			ByteArray = 0x20,
			Stream = 33,
			StartOfUserTypes = 0x40
		}

		/// <summary>Holds the number used to identify resource files.</summary>
		public const int MagicNumber = unchecked((int)0xBEEFCACE);
		const int ResourceSetVersion = 2;

		readonly MyBinaryReader reader;
		readonly int version;
		readonly int numResources;
		readonly string[] typeTable;
		readonly int[] namePositions;
		readonly long fileStartPosition;
		readonly long nameSectionPosition;
		readonly long dataSectionPosition;

		/// <summary>
		/// Creates a new ResourcesFile.
		/// </summary>
		/// <param name="stream">Input stream.</param>
		/// <param name="leaveOpen">Whether the stream should be help open when the ResourcesFile is disposed.</param>
		/// <remarks>
		/// The stream is must be held open while the ResourcesFile is in use.
		/// The stream must be seekable; any operation using the ResourcesFile will end up seeking the stream.
		/// </remarks>
		public MyResourceReader(Stream stream, bool leaveOpen = true)
		{
			fileStartPosition = stream.Position;
			reader = new MyBinaryReader(stream, leaveOpen);

			const string ResourcesHeaderCorrupted = "Resources header corrupted.";

			// Read ResourceManager header
			// Check for magic number
			int magicNum = reader.ReadInt32();
			if (magicNum != MagicNumber)
				throw new BadImageFormatException("Not a .resources file - invalid magic number");
			// Assuming this is ResourceManager header V1 or greater, hopefully
			// after the version number there is a number of bytes to skip
			// to bypass the rest of the ResMgr header. For V2 or greater, we
			// use this to skip to the end of the header
			int resMgrHeaderVersion = reader.ReadInt32();
			int numBytesToSkip = reader.ReadInt32();
			if (numBytesToSkip < 0 || resMgrHeaderVersion < 0)
			{
				throw new BadImageFormatException(ResourcesHeaderCorrupted);
			}
			if (resMgrHeaderVersion > 1)
			{
				reader.BaseStream.Seek(numBytesToSkip, SeekOrigin.Current);
			}
			else
			{
				// We don't care about numBytesToSkip; read the rest of the header

				// readerType:
				reader.ReadString();
				// resourceSetType:
				reader.ReadString();
			}

			// Read RuntimeResourceSet header
			// Do file version check
			version = reader.ReadInt32();
			if (version != ResourceSetVersion && version != 1)
				throw new BadImageFormatException($"Unsupported resource set version: {version}");

			numResources = reader.ReadInt32();
			if (numResources < 0)
			{
				throw new BadImageFormatException(ResourcesHeaderCorrupted);
			}

			// Read type positions into type positions array.
			// But delay initialize the type table.
			int numTypes = reader.ReadInt32();
			if (numTypes < 0)
			{
				throw new BadImageFormatException(ResourcesHeaderCorrupted);
			}
			typeTable = new string[numTypes];
			for (int i = 0; i < numTypes; i++)
			{
				typeTable[i] = reader.ReadString();
			}

			// Prepare to read in the array of name hashes
			//  Note that the name hashes array is aligned to 8 bytes so 
			//  we can use pointers into it on 64 bit machines. (4 bytes 
			//  may be sufficient, but let's plan for the future)
			//  Skip over alignment stuff.  All public .resources files
			//  should be aligned   No need to verify the byte values.
			long pos = reader.BaseStream.Position - fileStartPosition;
			int alignBytes = unchecked((int)pos) & 7;
			if (alignBytes != 0)
			{
				for (int i = 0; i < 8 - alignBytes; i++)
				{
					reader.ReadByte();
				}
			}

			// Skip over the array of name hashes
			try
			{
				reader.Seek(checked(4 * numResources), SeekOrigin.Current);
			}
			catch (OverflowException)
			{
				throw new BadImageFormatException(ResourcesHeaderCorrupted);
			}

			// Read in the array of relative positions for all the names.
			namePositions = new int[numResources];
			for (int i = 0; i < numResources; i++)
			{
				int namePosition = reader.ReadInt32();
				if (namePosition < 0)
				{
					throw new BadImageFormatException(ResourcesHeaderCorrupted);
				}
				namePositions[i] = namePosition;
			}

			// Read location of data section.
			int dataSectionOffset = reader.ReadInt32();
			if (dataSectionOffset < 0)
			{
				throw new BadImageFormatException(ResourcesHeaderCorrupted);
			}

			// Store current location as start of name section
			nameSectionPosition = reader.BaseStream.Position;
			dataSectionPosition = fileStartPosition + dataSectionOffset;

			// _nameSectionOffset should be <= _dataSectionOffset; if not, it's corrupt
			if (dataSectionPosition < nameSectionPosition)
			{
				throw new BadImageFormatException(ResourcesHeaderCorrupted);
			}
		}

		public void Dispose()
		{
			reader.Dispose();
		}

		string GetResourceName(int index, out int dataOffset)
		{
			long pos = nameSectionPosition + namePositions[index];
			byte[] bytes;
			lock (reader)
			{
				reader.Seek(pos, SeekOrigin.Begin);
				// Can't use reader.ReadString, since it's using UTF-8!
				int byteLen = reader.Read7BitEncodedInt();
				if (byteLen < 0)
				{
					throw new BadImageFormatException("Resource name has negative length");
				}
				bytes = new byte[byteLen];
				// We must read byteLen bytes, or we have a corrupted file.
				// Use a blocking read in case the stream doesn't give us back
				// everything immediately.
				int count = byteLen;
				while (count > 0)
				{
					int n = reader.Read(bytes, byteLen - count, count);
					if (n == 0)
						throw new BadImageFormatException("End of stream within a resource name");
					count -= n;
				}
				dataOffset = reader.ReadInt32();
				if (dataOffset < 0)
				{
					throw new BadImageFormatException("Negative data offset");
				}
			}
			return Encoding.Unicode.GetString(bytes);
		}

		object LoadObject(int dataOffset)
		{
			try
			{
				lock (reader)
				{
					if (version == 1)
					{
						return LoadObjectV1(dataOffset);
					}
					else
					{
						return LoadObjectV2(dataOffset);
					}
				}
			}
			catch (EndOfStreamException e)
			{
				throw new BadImageFormatException("Invalid resource file", e);
			}
		}

		string FindType(int typeIndex)
		{
			if (typeIndex < 0 || typeIndex >= typeTable.Length)
				throw new BadImageFormatException("Type index out of bounds");
			return typeTable[typeIndex];
		}

		// This takes a virtual offset into the data section and reads an Object
		// from that location.
		// Anyone who calls LoadObject should make sure they take a lock so 
		// no one can cause us to do a seek in here.
		private object LoadObjectV1(int dataOffset)
		{
			Debug.Assert(System.Threading.Monitor.IsEntered(reader));
			reader.Seek(dataSectionPosition + dataOffset, SeekOrigin.Begin);
			int typeIndex = reader.Read7BitEncodedInt();
			if (typeIndex == -1)
				return null;
			string typeName = FindType(typeIndex);
			int comma = typeName.IndexOf(',');
			if (comma > 0)
			{
				// strip assembly name
				typeName = typeName.Substring(0, comma);
			}
			switch (typeName)
			{
				case "System.String":
					return reader.ReadString();
				case "System.Byte":
					return reader.ReadByte();
				case "System.SByte":
					return reader.ReadSByte();
				case "System.Int16":
					return reader.ReadInt16();
				case "System.UInt16":
					return reader.ReadUInt16();
				case "System.Int32":
					return reader.ReadInt32();
				case "System.UInt32":
					return reader.ReadUInt32();
				case "System.Int64":
					return reader.ReadInt64();
				case "System.UInt64":
					return reader.ReadUInt64();
				case "System.Single":
					return reader.ReadSingle();
				case "System.Double":
					return reader.ReadDouble();
				case "System.DateTime":
					// Ideally we should use DateTime's ToBinary & FromBinary,
					// but we can't for compatibility reasons.
					return new DateTime(reader.ReadInt64());
				case "System.TimeSpan":
					return new TimeSpan(reader.ReadInt64());
				case "System.Decimal":
					int[] bits = new int[4];
					for (int i = 0; i < bits.Length; i++)
						bits[i] = reader.ReadInt32();
					return new decimal(bits);
				default:
					return new ResourceSerializedObject(FindType(typeIndex), this, reader.BaseStream.Position);
			}
		}

		private object LoadObjectV2(int dataOffset)
		{
			Debug.Assert(System.Threading.Monitor.IsEntered(reader));
			reader.Seek(dataSectionPosition + dataOffset, SeekOrigin.Begin);
			var typeCode = (ResourceTypeCode)reader.Read7BitEncodedInt();
			switch (typeCode)
			{
				case ResourceTypeCode.Null:
					return null;

				case ResourceTypeCode.String:
					return reader.ReadString();

				case ResourceTypeCode.Boolean:
					return reader.ReadBoolean();

				case ResourceTypeCode.Char:
					return (char)reader.ReadUInt16();

				case ResourceTypeCode.Byte:
					return reader.ReadByte();

				case ResourceTypeCode.SByte:
					return reader.ReadSByte();

				case ResourceTypeCode.Int16:
					return reader.ReadInt16();

				case ResourceTypeCode.UInt16:
					return reader.ReadUInt16();

				case ResourceTypeCode.Int32:
					return reader.ReadInt32();

				case ResourceTypeCode.UInt32:
					return reader.ReadUInt32();

				case ResourceTypeCode.Int64:
					return reader.ReadInt64();

				case ResourceTypeCode.UInt64:
					return reader.ReadUInt64();

				case ResourceTypeCode.Single:
					return reader.ReadSingle();

				case ResourceTypeCode.Double:
					return reader.ReadDouble();

				case ResourceTypeCode.Decimal:
					return reader.ReadDecimal();

				case ResourceTypeCode.DateTime:
					// Use DateTime's ToBinary & FromBinary.
					long data = reader.ReadInt64();
					return DateTime.FromBinary(data);

				case ResourceTypeCode.TimeSpan:
					long ticks = reader.ReadInt64();
					return new TimeSpan(ticks);

				// Special types
				case ResourceTypeCode.ByteArray:
					{
						int len = reader.ReadInt32();
						if (len < 0)
						{
							throw new BadImageFormatException("Resource with negative length");
						}
						return reader.ReadBytes(len);
					}

				case ResourceTypeCode.Stream:
					{
						int len = reader.ReadInt32();
						if (len < 0)
						{
							throw new BadImageFormatException("Resource with negative length");
						}
						byte[] bytes = reader.ReadBytes(len);
						return new MemoryStream(bytes, writable: false);
					}

				default:
					if (typeCode < ResourceTypeCode.StartOfUserTypes)
					{
						throw new BadImageFormatException("Invalid typeCode");
					}
					return new ResourceSerializedObject(FindType(typeCode - ResourceTypeCode.StartOfUserTypes), this, reader.BaseStream.Position);
			}
		}

		public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
		{
			for (int i = 0; i < numResources; i++)
			{
				string name = GetResourceName(i, out int dataOffset);
				object val = LoadObject(dataOffset);
				yield return new KeyValuePair<string, object>(name, val);
			}
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		public class ResourceSerializedObject
		{
			public string TypeName { get; }
			readonly MyResourceReader file;
			readonly long position;

			internal ResourceSerializedObject(string typeName, MyResourceReader file, long position)
			{
				this.TypeName = typeName;
				this.file = file;
				this.position = position;
			}
		}
	}
}
