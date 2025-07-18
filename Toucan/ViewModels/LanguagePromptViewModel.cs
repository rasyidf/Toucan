﻿using CommunityToolkit.Mvvm.ComponentModel;
using Toucan.Core;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Toucan.ViewModels;



public partial class LanguagePromptViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<LanguageModel> cultureList;

    [ObservableProperty]
    private LanguageModel language;

    public LanguagePromptViewModel()
    {
        CultureList = new(
            CultureInfo.GetCultures(CultureTypes.AllCultures)
            .OrderBy(c => c.DisplayName)
            .Select(c => new LanguageModel { Culture = c, Language = c.DisplayName })
            .ToList()
           );
    }

}
