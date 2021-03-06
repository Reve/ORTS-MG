﻿// COPYRIGHT 2014 by the Open Rails project.
// 
// This file is part of Open Rails.
// 
// Open Rails is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// Open Rails is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with Open Rails.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Orts.Settings.Store;

namespace Orts.Settings
{
    public class UpdateSettings : SettingsBase
    {
        private static readonly string settingsFilePath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "Updater.ini");

        #region Update Settings

        // Please put all update settings in here as auto-properties. Public properties
        // of type 'string', 'int', byte, 'bool', 'string[]' and 'int[]', TimeSpan and DateTime are automatically loaded/saved.

        [Default("")]
        public string Channel { get; set; }
        [Default("")]
        public string URL { get; set; }
        [Default(86400)]
        public TimeSpan TTL { get; set; }
        [Default("")]
        public string ChangeLogLink { get; set; }

        #endregion

        public UpdateSettings(StoreType storeType, string location)
            : base(SettingsStore.GetSettingsStore(storeType, location, "Settings"))
        {
            LoadSettings(new string[0]);
        }

        public UpdateSettings()
            : base(SettingsStore.GetSettingsStore(StoreType.Ini, settingsFilePath, "Settings"))
        {
            LoadSettings(new string[0]);
        }

        public UpdateSettings(string channel)
            : base(SettingsStore.GetSettingsStore(StoreType.Ini, settingsFilePath, channel + "Settings"))
        {
            LoadSettings(new string[0]);
        }

        public string[] GetChannels()
        {
            return (from name in SettingStore.GetSectionNames()
                    where name.EndsWith("Settings")
                    select name.Replace("Settings", "")).ToArray();
        }

        public override object GetDefaultValue(string name)
        {
            var property = GetProperty(name);

            var attributes = property.GetCustomAttributes(typeof(DefaultAttribute), false);
            if (attributes.Length > 0)
                return (name == nameof(TTL)) ? TimeSpan.FromSeconds((int)(attributes[0] as DefaultAttribute)?.Value) : (attributes[0] as DefaultAttribute)?.Value;

            throw new InvalidDataException($"UserSetting {property.Name} has no default value.");
        }

        protected override object GetValue(string name)
        {
            return GetProperty(name).GetValue(this, null);
        }

        protected override void SetValue(string name, object value)
        {
            GetProperty(name).SetValue(this, value, null);
        }

        protected override void Load(bool allowUserSettings, NameValueCollection optionalValues)
        {
            foreach (var property in GetProperties())
                LoadSetting(allowUserSettings, optionalValues, property.Name);
            properties = null;
        }

        public override void Save()
        {
            foreach (var property in GetProperties())
                Save(property.Name);
            properties = null;
        }

        public override void Save(string name)
        {
            if (AllowPropertySaving(name))
                SaveSetting(name);
        }

        public override void Reset()
        {
            foreach (var property in GetProperties())
                Reset(property.Name);
        }
    }
}
