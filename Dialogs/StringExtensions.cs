namespace Microsoft.Bot.Sample.BootCamp.Dialogs
{
    public static class StringExtensions
    {
        public static string RemoverPontuacao(this string target)
        {
            return target.Replace(".", "").Replace("!", "").Replace("?", "").Replace(";","");
        }
    }
}