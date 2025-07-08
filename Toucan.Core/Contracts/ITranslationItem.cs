namespace OPEdit.Core.Contracts;

public interface ITranslationItem
{
    string Namespace { get; set; }
    string Value { get; set; }
    string Language { get; set; }
}


