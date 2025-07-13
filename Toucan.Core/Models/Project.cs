using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Toucan.Core.Models;
public class Project
{
    public string Path { get; set; }
    public string Name => System.IO.Path.GetFileName(Path);
    public DateTime LastOpened { get; set; }

    public bool IsValid()
    {
        if (!Directory.Exists(Path)) return false;


        return true;
    }
}