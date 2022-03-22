using System.IO;
using System.Reflection;
using yay_see_sharp.application.Extenstions;

namespace yay_see_sharp.application.Helpers
{
    internal static class Constants
    {
        private static string? myApplicationDirectory;
        private static string? myAssetsDirectory;

        #region Constants
        public const string ApplicationName = "ApplicationName";
        public const string Assets = "Assets";
        #endregion

        #region Helpers Methods
        public static string GetApplicationName()
        {
            return ApplicationName.GetResourceString();
        }

        public static string GetApplicationDirectory()
        {
            if(string.IsNullOrEmpty(myApplicationDirectory))
            {
                var location = Assembly.GetExecutingAssembly().Location;
                if(string.IsNullOrEmpty(location))
                {
                    myApplicationDirectory = string.Empty;
                }
                else
                {
                    var directoryName = Path.GetDirectoryName(location);
                    if(string.IsNullOrEmpty(directoryName))
                    {
                        myApplicationDirectory = string.Empty;
                    }
                    else
                    {
                        myApplicationDirectory = directoryName;
                    }
                }
            }
            return myApplicationDirectory;
        }

        public static string GetAssetsDirectory()
        {
            if(string.IsNullOrEmpty(myAssetsDirectory))
            {
                var applicationDirectory = GetApplicationDirectory();
                if (!string.IsNullOrEmpty(applicationDirectory))
                {
                    myAssetsDirectory = Path.Combine(applicationDirectory, Assets);
                }
                else
                {
                    myAssetsDirectory = string.Empty;
                }
            }
            return myAssetsDirectory;
        }
        #endregion
    }
}
