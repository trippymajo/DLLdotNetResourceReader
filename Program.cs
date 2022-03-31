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
			var path = @"C:\Users\Admin\Desktop\CefSharp.dll"; // Your path to dll file
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
						var RR = new ResourceReader(stream);
						IDictionaryEnumerator dict = RR.GetEnumerator();
						while (dict.MoveNext())
							Console.WriteLine("   {0}: '{1}' (Type {2})", dict.Key, dict.Value, dict.Value.GetType().Name); //Output
						RR.Close();
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