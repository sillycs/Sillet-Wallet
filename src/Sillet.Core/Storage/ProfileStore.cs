using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sillet.Net;

namespace Sillet.Storage
{
    public sealed class Profile
    {
        public Profile(string id, string name, string kind, IDictionary<string, string> watch)
        {
            Id = id;
            Name = name;
            Kind = kind;
            Watch = new Dictionary<string, string>(watch ?? new Dictionary<string, string>());
        }

        public string Id { get; }
        public string Name { get; set; }
        public string Kind { get; }
        public Dictionary<string, string> Watch { get; }
        public bool IsWatch => Kind == "watch";
        public bool HasAddresses => Watch.Count > 0;
        public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name.Substring(0, 1).ToUpperInvariant();
        public string PhotoPath => AvatarStore.PathFor(Id) ?? AvatarStore.PathFor("acc_" + Id + "_0");
        public bool HasPhoto => PhotoPath != null;
        public string KindText => IsWatch ? "Hardware wallet" : "Recovery phrase";
    }

    public sealed class ProfileStore
    {
        public const string MainId = "main";
        private readonly string _folder;
        private readonly string _path;
        private readonly List<Profile> _items = new List<Profile>();
        private string _activeId;

        public ProfileStore(string folder = null)
        {
            _folder = folder ?? Path.GetDirectoryName(Vault.DefaultPath);
            _path = Path.Combine(_folder, "profiles.json");
            AvatarStore.Folder = Path.Combine(_folder, "avatars");
            Load();
        }

        public IReadOnlyList<Profile> Items => _items;

        public Profile Active
        {
            get { return _items.FirstOrDefault(p => p.Id == _activeId) ?? _items.FirstOrDefault(); }
        }

        public string VaultPath(Profile profile)
        {
            if (profile == null || profile.Id == MainId) return Path.Combine(_folder, "vault.json");
            return Path.Combine(_folder, "vault-" + profile.Id + ".json");
        }

        public Profile NewSeed()
        {
            var id = _items.All(p => p.Id != MainId) && !File.Exists(VaultPath(null)) ? MainId : Guid.NewGuid().ToString("N").Substring(0, 12);
            return new Profile(id, NextName(), "seed", null);
        }

        public Profile NewWatch(string name, IDictionary<string, string> addresses)
        {
            var clean = addresses.Where(a => !string.IsNullOrWhiteSpace(a.Value)).ToDictionary(a => a.Key, a => a.Value.Trim());
            return new Profile(Guid.NewGuid().ToString("N").Substring(0, 12), string.IsNullOrWhiteSpace(name) ? "Hardware wallet" : name.Trim(), "watch", clean);
        }

        public string NextName()
        {
            if (_items.Count == 0) return "Main wallet";
            for (int n = _items.Count + 1; ; n++)
            {
                var name = "Wallet " + n;
                if (_items.All(p => p.Name != name)) return name;
            }
        }

        public void Add(Profile profile)
        {
            if (_items.Any(p => p.Id == profile.Id)) return;
            _items.Add(profile);
            Save();
        }

        public void SetActive(Profile profile)
        {
            if (profile == null) return;
            _activeId = profile.Id;
            Save();
        }

        public void Rename(Profile profile, string name)
        {
            if (profile == null || string.IsNullOrWhiteSpace(name)) return;
            profile.Name = name.Trim().Length > 32 ? name.Trim().Substring(0, 32) : name.Trim();
            Save();
        }

        public void SetAddresses(Profile profile, IDictionary<string, string> addresses)
        {
            if (profile == null || profile.IsWatch || addresses == null) return;
            if (profile.Watch.Count == addresses.Count && addresses.All(a => profile.Watch.ContainsKey(a.Key) && profile.Watch[a.Key] == a.Value)) return;
            profile.Watch.Clear();
            foreach (var a in addresses) profile.Watch[a.Key] = a.Value;
            Save();
        }

        public void Remove(Profile profile)
        {
            if (profile == null) return;
            _items.RemoveAll(p => p.Id == profile.Id);
            if (_activeId == profile.Id) _activeId = _items.Count > 0 ? _items[0].Id : null;
            Save();
        }

        public void EnsureMain()
        {
            if (_items.Count > 0 || !File.Exists(VaultPath(null))) return;
            _items.Add(new Profile(MainId, "Main wallet", "seed", null));
            _activeId = MainId;
            Save();
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_path))
                {
                    var root = JsonValue.Parse(LocalData.ReadText(_path));
                    JsonValue value;
                    if (root.TryGet("Active", out value) && value.IsString) _activeId = value.AsString();
                    if (root.TryGet("Profiles", out value) && value.IsArray)
                    {
                        foreach (var item in value.Items)
                        {
                            JsonValue id, name, kind, watch;
                            if (!item.TryGet("Id", out id) || !id.IsString) continue;
                            var map = new Dictionary<string, string>();
                            if (item.TryGet("Watch", out watch) && watch.IsArray)
                                foreach (var w in watch.Items)
                                {
                                    JsonValue g, a;
                                    if (w.TryGet("Group", out g) && w.TryGet("Address", out a) && g.IsString && a.IsString) map[g.AsString()] = a.AsString();
                                }
                            _items.Add(new Profile(id.AsString(), item.TryGet("Name", out name) && name.IsString ? name.AsString() : "Wallet",
                                item.TryGet("Kind", out kind) && kind.IsString ? kind.AsString() : "seed", map));
                        }
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is FormatException || e is InvalidOperationException)
            {
                _items.Clear();
            }
            EnsureMain();
        }

        private void Save()
        {
            try
            {
                LocalData.WriteText(_path, Json.Serialize(new
                {
                    Active = _activeId ?? "",
                    Profiles = _items.Select(p => new
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Kind = p.Kind,
                        Watch = p.Watch.Select(w => new { Group = w.Key, Address = w.Value }).ToList(),
                    }).ToList(),
                }));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
        }
    }

    public static class WatchFile
    {
        public const string Format = "sillet-watch-1";

        public static string Serialize(string name, IDictionary<string, string> addresses)
        {
            return Json.Serialize(new
            {
                Format = Format,
                Name = name ?? "Wallet",
                Addresses = addresses.Select(a => new { Group = a.Key, Address = a.Value }).ToList(),
            });
        }

        public static bool TryParse(string text, out string name, out Dictionary<string, string> addresses)
        {
            name = null;
            addresses = new Dictionary<string, string>();
            try
            {
                var root = JsonValue.Parse(text);
                JsonValue format, list, n;
                if (!root.IsObject || !root.TryGet("Format", out format) || !format.IsString || format.AsString() != Format) return false;
                if (root.TryGet("Name", out n) && n.IsString) name = n.AsString();
                if (!root.TryGet("Addresses", out list) || !list.IsArray) return false;
                foreach (var item in list.Items)
                {
                    JsonValue g, a;
                    if (item.TryGet("Group", out g) && item.TryGet("Address", out a) && g.IsString && a.IsString) addresses[g.AsString()] = a.AsString().Trim();
                }
                return addresses.Count > 0;
            }
            catch (Exception e) when (e is FormatException || e is InvalidOperationException)
            {
                return false;
            }
        }
    }
}
