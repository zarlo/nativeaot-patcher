using System;
using System.Diagnostics;
using System.IO;

var paths = Environment.GetEnvironmentVariable("PATH").Split(Path.PathSeparator);
string yasmPath = null;
foreach (var p in paths) {
    var fullPath = Path.Combine(p, "yasm");
    if (File.Exists(fullPath)) {
        yasmPath = fullPath;
        break;
    }
}
Console.WriteLine(yasmPath ?? "NOT FOUND");
