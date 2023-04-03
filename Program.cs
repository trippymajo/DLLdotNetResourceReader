using DLLdotNetResourceReader;
using System;
using System.Linq;
using System.Reflection;

//using System.Resources.Extensions;

namespace DLLResourceReader
{
	class Program
	{
		
		static void Main(string[] args)
		{
			var path = @"PATH"; // Your path to dll file
			try
			{
				var assembly = Assembly.ReflectionOnlyLoadFrom(path);
				var names = assembly.GetManifestResourceNames();
				if (names.Count() == 0)
				{
					Console.WriteLine("No Resources");
					Console.ReadKey();
				}
				else
				{
					for (int i = 0; i <= names.Count() - 1; i++)
					{
						Console.WriteLine("\n\n{0}", names[i]);
						var stream = assembly.GetManifestResourceStream(names[i]);
						var RR = new MyResourceReader(stream);
						foreach (var entry in RR) 
						{
							if (entry.Value is string)
							{
								Console.WriteLine(" ID: {0} - Value: '{1}' ", entry.Key, (string)entry.Value);
							}
						}
					}
				}
			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
			}
			Console.ReadKey();
		}
	}
}