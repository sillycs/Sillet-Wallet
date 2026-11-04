using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Sillet.Storage
{
    /// <summary>Profile photos picked by the user. They stay on this device, next to the profiles, and are never uploaded anywhere.</summary>
    public static class AvatarStore
    {
        public const int MaxBytes = 3 * 1024 * 1024;
        private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>();
        private static readonly object Gate = new object();

        /// <summary>Set by the profile store. Null until then, so nothing is found or written.</summary>
        public static string Folder { get; set; }

        public static string PathFor(string id)
        {
            if (string.IsNullOrEmpty(Folder) || string.IsNullOrEmpty(id)) return null;
            lock (Gate)
            {
                string cached;
                if (Cache.TryGetValue(id, out cached)) return cached;
                string found = null;
                try
                {
                    if (Directory.Exists(Folder))
                        found = Directory.GetFiles(Folder, id + "-*.img").OrderByDescending(f => f, StringComparer.Ordinal).FirstOrDefault();
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
                Cache[id] = found;
                return found;
            }
        }

        /// <summary>True when the bytes look like a PNG or a JPEG of a sensible size.</summary>
        public static bool IsImage(byte[] data)
        {
            if (data == null || data.Length < 16 || data.Length > MaxBytes) return false;
            bool png = data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47;
            bool jpeg = data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;
            return png || jpeg;
        }

        /// <summary>Stores the photo under a new name each time, so image caches never show the old one. Returns false when it is not a usable image.</summary>
        public static bool Save(string id, byte[] data)
        {
            if (string.IsNullOrEmpty(Folder) || string.IsNullOrEmpty(id) || !IsImage(data)) return false;
            try
            {
                Directory.CreateDirectory(Folder);
                Remove(id);
                File.WriteAllBytes(Path.Combine(Folder, id + "-" + DateTime.UtcNow.Ticks + ".img"), data);
                lock (Gate) Cache.Remove(id);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        public static void Remove(string id)
        {
            if (string.IsNullOrEmpty(Folder) || string.IsNullOrEmpty(id)) return;
            try
            {
                if (Directory.Exists(Folder))
                    foreach (var file in Directory.GetFiles(Folder, id + "-*.img")) File.Delete(file);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            lock (Gate) Cache.Remove(id);
        }
    }
}
