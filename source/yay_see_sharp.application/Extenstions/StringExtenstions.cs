using yay_see_sharp.application.Properties;

namespace yay_see_sharp.application.Extenstions
{
    internal static class StringExtenstions
    {
        public static string GetResourceString(this string resourceName)
        {
            var resourceString = Resources.ResourceManager.GetString(resourceName);
            if(string.IsNullOrEmpty(resourceString))
            {
                resourceString = string.Empty;
            }
            return resourceString;
        }
    }
}
