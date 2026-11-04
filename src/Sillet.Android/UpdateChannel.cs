using Android.Content;
using Sillet.Mobile;

namespace Sillet.Droid
{
    /// <summary>
    /// Tells the shared update checker which version this APK is (from the manifest) and to read version-android.json,
    /// so an Android only release does not touch what Windows users see.
    /// </summary>
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
