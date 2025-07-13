using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Toucan.Core.Models;

public class TranslationItem
{
    public string Language { get; set; }
    public string Namespace { get; set; }
    public string Value { get; set; }
}
