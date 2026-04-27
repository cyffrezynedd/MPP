using System;
using System.IO;

namespace Lab.Shared
{
    public static class TestPaths
    {
        public static string ProjectTestsDll()
        {
            var p = Path.Combine(AppContext.BaseDirectory, "project.tests.dll");
            if (!File.Exists(p))
                throw new FileNotFoundException("Нет project.tests.dll рядом с exe — сначала собери sln.", p);
            return p;
        }
    }
}
