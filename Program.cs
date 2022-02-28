using System;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Collections;

namespace DLLResourceReader
{
	class Program
	{
		static void Main(string[] args)
		{
			var path = @"D:\Documents\C\REPOS\DLLResourceReader\DLLResourceReader\bin\Debug\DWGConverterCP.dll"; // Your path to dll file
			var assembly = Assembly.ReflectionOnlyLoadFrom(path);
			var names = assembly.GetManifestResourceNames();
			for (int i=0; i < names.Count() - 1; i++)
			{
				var stream = assembly.GetManifestResourceStream(names[i]);
				var RR = new ResourceReader(stream);
				IDictionaryEnumerator dict = RR.GetEnumerator();
				while (dict.MoveNext())
					Console.WriteLine("   {0}: '{1}' (Type {2})", dict.Key, dict.Value, dict.Value.GetType().Name); //Output
				RR.Close();
			}
			Console.ReadKey();
		}
	}
}
