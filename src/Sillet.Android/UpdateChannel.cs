using Android.Content;
using Sillet.Mobile;

namespace Sillet.Droid
{
    public static class UpdateChannel
    {
        public static void Apply(Context context)
        {
            string name = null;
            try
            {
                name = context.PackageManager.GetPackageInfo(context.PackageName, 0).VersionName;
            }
            catch (Android.Content.PM.PackageManager.NameNotFoundException)
            {
            }
            MobilePlatform.ConfigureUpdates(name);
        }
    }
}
