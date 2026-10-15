namespace Localization.Base
{
    public interface ILocalizeText
    {
        public string LocalizationKey { get; set; }
        // protected static char LineEnding = '\n';
        public bool DontChangeAlignment { get; set; }

        public void UpdateTextLocalization();
    }
}
