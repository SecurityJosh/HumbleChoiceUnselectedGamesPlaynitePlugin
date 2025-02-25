using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Security.Cryptography;
using HumbleChoice;

namespace HumbleChoiceUnselected
{
    public class HumbleChoiceUnselectedSettings : ObservableObject
    {
        private string cookie = String.Empty;
        public string Cookie { get; set; }
        private bool importEntitlements = false;
        public bool ImportEntitlements { get; set; }

        // Playnite serializes settings object to a JSON object and saves it as text file.
        // If you want to exclude some property from being saved then use `JsonDontSerialize` ignore attribute.
        //[DontSerialize]
    }

    public class HumbleChoiceUnselectedSettingsViewModel : ObservableObject, ISettings
    {
        private readonly HumbleChoiceUnselected plugin;
        private HumbleChoiceUnselectedSettings editingClone { get; set; }

        private HumbleChoiceUnselectedSettings settings;
        public HumbleChoiceUnselectedSettings Settings
        {
            get => settings;
            set
            {
                settings = value;
                OnPropertyChanged();
            }
        }

        public HumbleChoiceUnselectedSettingsViewModel(HumbleChoiceUnselected plugin)
        {
            // Injecting your plugin instance is required for Save/Load method because Playnite saves data to a location based on what plugin requested the operation.
            this.plugin = plugin;

            // Load saved settings.
            var savedSettings = plugin.LoadPluginSettings<HumbleChoiceUnselectedSettings>();

            // LoadPluginSettings returns null if no saved data is available.
            if (savedSettings != null)
            {
                Settings = savedSettings;
            }
            else
            {
                Settings = new HumbleChoiceUnselectedSettings();
            }
        }

        public void BeginEdit()
        {
            // Code executed when settings view is opened and user starts editing values.
            editingClone = Serialization.GetClone(Settings);

            // TODO: Do I need to deserialize the new bool?
            if (Settings != null)
            {
                if (Settings.Cookie != null && Settings.Cookie.Length > 0)
                {
                    var decryptedCookie = Dpapi.Unprotect(Settings.Cookie, plugin.Id.ToString(), DataProtectionScope.CurrentUser);

                    if (decryptedCookie != null)
                    {
                        Settings.Cookie = decryptedCookie;
                    }
                    else
                    {
                        plugin.GetLogger().Error("Error decrypting cookie");
                        Settings.Cookie = "";
                    }
                }

            }
        }

        public void CancelEdit()
        {
            // Code executed when user decides to cancel any changes made since BeginEdit was called.
            // This method should revert any changes made to Option1 and Option2.
            Settings = editingClone;
        }

        public void EndEdit()
        {
            var encryptedCookie = Dpapi.Protect(Settings.Cookie, plugin.Id.ToString(), DataProtectionScope.CurrentUser);

            if (encryptedCookie != null)
            {
                Settings.Cookie = encryptedCookie;
            }
            else
            {
                plugin.GetLogger().Error("Error encrypting cookie");
                Settings.Cookie = "";
            }

            // Code executed when user decides to confirm changes made since BeginEdit was called.
            // This method should save settings made to Option1 and Option2.
            plugin.SavePluginSettings(Settings);
        }

        public bool VerifySettings(out List<string> errors)
        {
            // Code execute when user decides to confirm changes made since BeginEdit was called.
            // Executed before EndEdit is called and EndEdit is not called if false is returned.
            // List of errors is presented to user if verification fails.
            errors = new List<string>();

            if (Settings.Cookie?.Length > 0 && !Regex.IsMatch(settings.Cookie, @"^ey[a-zA-Z0-9+=]+\|\d+\|[a-f0-9]{40}$"))
            {
                errors.Add("Cookie does not match expected format");
            }

            return errors.Count == 0;
        }
    }
}
