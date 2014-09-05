using CodecoreTechnologies.Elve.DriverFramework;
using CodecoreTechnologies.Elve.DriverFramework.Communication;
using CodecoreTechnologies.Elve.DriverFramework.DeviceSettingEditors;
using CodecoreTechnologies.Elve.DriverFramework.DriverInterfaces;
using CodecoreTechnologies.Elve.DriverFramework.Scripting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Timers;
using System.Xml.Linq;

namespace OnClickInc.Elve
{
	[Driver("Xantech DIGI-5 Driver", "A driver for monitoring and controlling a Xantech DIGI-5 Audio Distribution System.", "Ryan Melena", "Multi-Room Audio", "", "Digi5", DriverCommunicationPort.Serial, DriverMultipleInstances.OnePerDriverService, 0, 1, DriverReleaseStages.Development, "Xantech", "http://www.xantech.com/", null)]
	public class XantechDigi5Driver : Driver, IMultiroomAudioDriver
	{
		#region Constants

		protected const int MAX_BALANCE = 5;
		protected const int MAX_SOURCE_NUMBER = 5;
		protected const int MAX_VOLUME = 21;
		protected const int MAX_ZONE_NUMBER = 6;
		protected const int MIN_BALANCE = 5;
		protected const int MIN_SOURCE_NUMBER = 1;
		protected const int MIN_VOLUME = 1;
		protected const int MIN_ZONE_NUMBER = 1;

		#endregion Constants

		#region Fields

		protected ICommunication _comm;
		protected string _firmwareVersion = String.Empty;
		protected string[] _sourceNames = new string[6];
		protected Timer _updateStatusTimer;
		protected string[] _zoneNames = new string[6];
		protected Zone[] _zones;

		#endregion Fields

		#region DriverSettings

		[DriverSetting("Refresh Interval", "Interval in seconds between status update requests.  Values update asynchronously when changed via Elve.", 1D, double.MaxValue, "1", true)]
		public int RefreshIntervalSetting { get; set; }

		[DriverSetting("Serial Port", "The serial port used to communicate with the Xantech DIGI-5.", "COM1", true)]
		public string SerialPortSetting { get; set; }

		[DriverSettingArrayNames("Source Names", "User-defined friendly names for each source.", typeof(ArrayItemsDriverSettingEditor), "SourceNames", MIN_SOURCE_NUMBER, MAX_SOURCE_NUMBER, "", false)]
		public string SourceNamesSetting
		{
			set
			{
				if (!string.IsNullOrEmpty(value))
				{
					XElement element = XElement.Parse(value);
					this._sourceNames = element.Elements("Item").Select(e => e.Attribute("Name").Value).ToArray();
				}
			}
		}

		[DriverSetting("Zone Count", "Number of zones supported by device.", new string[] { "4", "6" }, "4", true)]
		public int ZoneCountSetting { get; set; }

		[DriverSettingArrayNames("Zone Names", "User-defined friendly names for each zone.", typeof(ArrayItemsDriverSettingEditor), "ZoneNames", MIN_ZONE_NUMBER, MAX_ZONE_NUMBER, "", false)]
		public string ZoneNamesSetting
		{
			set
			{
				if (!string.IsNullOrEmpty(value))
				{
					XElement element = XElement.Parse(value);
					this._zoneNames = element.Elements("Item").Select(e => e.Attribute("Name").Value).ToArray();
				}
			}
		}

		#endregion DriverSettings

		#region ScriptObjectProperties

		#region IMatrixSwitcherDriver Members

		[ScriptObjectPropertyAttribute("Source Names", "Gets an array of all source names.", "the {NAME} source name for item #{INDEX|1}", null)]
		public IScriptArray SourceNames { get { return new ScriptArrayMarshalByValue(this._sourceNames, 1); } }

		[ScriptObjectPropertyAttribute("Zone Names", "Gets an array of all zone names.", "the {NAME} zone name for item #{INDEX|1}", null)]
		public IScriptArray ZoneNames { get { return new ScriptArrayMarshalByValue(this._zoneNames, 1); } }

		[ScriptObjectPropertyAttribute("Zone Source Names", "Gets an array of the currently selected source name for each zone.", "the {NAME} zone source name for item #{INDEX|1}", null)]
		[SupportsDriverPropertyBinding("Zone Source Name Changed", "Occurs when the currently selected source for a zone changes.")]
		public IScriptArray ZoneSourceNames { get { return new ScriptArrayMarshalByReference(this._zones.Select(z => (z.Source > 0 && z.Source <= this._sourceNames.Length) ? this._sourceNames[z.Source - 1] : String.Empty), new ScriptArraySetScriptStringCallback(this.SetZoneSourceByName), 1); } }

		[ScriptObjectPropertyAttribute("Zone Sources", "Gets an array of the currently selected source index for each zone.", "the {NAME} zone source index for item #{INDEX|1}", null)]
		[SupportsDriverPropertyBinding("Zone Source Index Changed", "Occurs when the currently selected source for a zone changes.")]
		public IScriptArray ZoneSources { get { return new ScriptArrayMarshalByReference(this._zones.Select(z => z.Source), new ScriptArraySetScriptNumberCallback(this.SetZoneSource), 1); } }

		#endregion IMatrixSwitcherDriver Members

		#region IMultiroomAudioDriver Members

		[ScriptObjectPropertyAttribute("Zone Mute States", "Gets an array of zone mute states.", "the {NAME} zone mute state for item #{INDEX|1}", null)]
		[SupportsDriverPropertyBinding("Zone Mute State Changed.", "Occurs when the mute state of a zone changes.")]
		public IScriptArray ZoneMuteStates { get { return new ScriptArrayMarshalByReference(this._zones.Select(z => z.IsMuted), new ScriptArraySetScriptBooleanCallback(this.SetZoneMuteState), 1); } }

		[ScriptObjectPropertyAttribute("Zone Power States", "Gets an array of zone power states.", "the {NAME} zone power state for item #{INDEX|1}", null)]
		[SupportsDriverPropertyBinding("Zone Power State Changed", "Occurs when the power state of a zone changes.")]
		public IScriptArray ZonePowerStates { get { return new ScriptArrayMarshalByReference(this._zones.Select(z => z.IsPowerOn), new ScriptArraySetScriptBooleanCallback(this.SetZonePowerState), 1); } }

		[ScriptObjectPropertyAttribute("Zone Volumes", "Gets an array of zone volumes.", "the {NAME} zone volume for item #{INDEX|1}", null)]
		[SupportsDriverPropertyBinding("Zone Volume Changed", "Occurs when the volume of a zone changes.")]
		public IScriptArray ZoneVolumes { get { return new ScriptArrayMarshalByReference(this._zones.Select(z => z.Volume), new ScriptArraySetScriptNumberCallback(this.SetZoneVolume), 1); } }

		#endregion IMultiroomAudioDriver Members

		[ScriptObjectPropertyAttribute("Device Code", "Gets the device code.", "the {NAME} device code", null)]
		public ScriptString DeviceCode { get; protected set; }

		[ScriptObjectPropertyAttribute("Device Type", "Gets the device type.", "the {NAME} device type", null)]
		public ScriptString DeviceType { get; protected set; }

		[ScriptObjectPropertyAttribute("Firmware Version", "Gets the firmware version.", "the {NAME} firmware version", null)]
		public ScriptString FirmwareVersion
		{
			get { return new ScriptString(this._firmwareVersion); }

			protected set
			{
				if (value != null)
				{
					this._firmwareVersion = value.ToPrimitiveString();

					int firmwareVersion = 0;
					string[] firmwareVersionParts = this._firmwareVersion.Split('.');

					if (firmwareVersionParts[0] != this._firmwareVersion)
					{
						this.Logger.Debug("Building Integer Firmware Version.");

						foreach (string firmwareVersionPart in this.FirmwareVersion.ToPrimitiveString().Split('.'))
						{
							firmwareVersion = firmwareVersion * 10 + Int32.Parse(firmwareVersionPart);
						}
					}

					this.Logger.Debug("Firmware Version Found [" + firmwareVersion.ToString() + "].");

					foreach (Zone zone in this._zones)
					{
						zone.HubFirmwareVersion = firmwareVersion;
					}
				}
			}
		}

		[ScriptObjectPropertyAttribute("Hardware Code", "Gets the hardware code.", "the {NAME} hardware code", null)]
		public ScriptString HardwareCode { get; protected set; }

		[ScriptObjectPropertyAttribute("Last Updated", "Gets the date and time of the last successful status update.", "the date and time of the last successful status update of {NAME}", null)]
		public ScriptDateTime LastUpdated { get; protected set; }

		[ScriptObjectPropertyAttribute("Zone Balances", "Gets an array of the current balance for each zone.", "the {NAME} zone balance for item #{INDEX|1}", null)]
		[SupportsDriverPropertyBinding("Zone Balance Changed", "Occurs when the balance for a zone changes.")]
		public IScriptArray ZoneBalances { get { return new ScriptArrayMarshalByReference(this._zones.Select(z => z.Balance), new ScriptArraySetScriptNumberCallback(this.SetZoneBalance), 1); } }

		[ScriptObjectPropertyAttribute("Zone Bass Levels", "Gets an array of the current bass level for each zone.", "the {NAME} zone bass level for item #{INDEX|1}", null)]
		[SupportsDriverPropertyBinding("Zone Bass Levels Changed", "Occurs when the bass level for a zone changes.")]
		public IScriptArray ZoneBassLevels { get { return new ScriptArrayMarshalByReference(this._zones.Select(z => z.Balance), new ScriptArraySetScriptNumberCallback(this.SetZoneBalance), 1); } }

		[ScriptObjectPropertyAttribute("Zone Treble Levels", "Gets an array of the current treble level for each zone.", "the {NAME} zone treble level for item #{INDEX|1}", null)]
		[SupportsDriverPropertyBinding("Zone Treble Levels Changed", "Occurs when the treble level for a zone changes.")]
		public IScriptArray ZoneTrebleLevels { get { return new ScriptArrayMarshalByReference(this._zones.Select(z => z.Balance), new ScriptArraySetScriptNumberCallback(this.SetZoneBalance), 1); } }

		#endregion ScriptObjectProperties

		#region Methods

		#region ScriptObjectMethods

		#region IMatrixSwitcherDriver Members

		[ScriptObjectMethod("Cycle Zone Source", "Increment the currently selected source on a zone.", "Increment the currently selected source for zone {PARAM|0|1} on {NAME}.")]
		[ScriptObjectMethodParameter("ZoneNumber", "The number of the zone.", MIN_ZONE_NUMBER, MAX_ZONE_NUMBER, "ZoneNames")]
		public void CycleZoneSource(ScriptNumber zoneNumber)
		{
			int zoneIndex = zoneNumber.ToPrimitiveInt32() - 1;
			if (zoneIndex < this._zones.Length)
			{
				Zone zone = this._zones[zoneIndex];
				zone.IncrementSource();
			}
			else
			{
				this.Logger.Error("Invalid ZoneNumber [" + zoneNumber.ToString() + "] in CycleZoneSource call.");
			}
		}

		[ScriptObjectMethod("Set Zone Source", "Set the currently selected source on a zone.", "Set the currently selected source to {PARAM|1|1} for zone {PARAM|0|1} on {NAME}.")]
		[ScriptObjectMethodParameter("ZoneNumber", "The number of the zone.", MIN_ZONE_NUMBER, MAX_ZONE_NUMBER, "ZoneNames")]
		[ScriptObjectMethodParameter("SourceNumber", "The number of the source to select.", MIN_SOURCE_NUMBER, MAX_SOURCE_NUMBER, "SourceNames")]
		public void SetZoneSource(ScriptNumber zoneNumber, ScriptNumber sourceNumber)
		{
			int zoneIndex = zoneNumber.ToPrimitiveInt32() - 1;
			if (zoneIndex < this._zones.Length)
			{
				this._zones[zoneIndex].SetSource(sourceNumber.ToPrimitiveInt32());
			}
			else
			{
				this.Logger.Error("Invalid ZoneNumber [" + zoneNumber.ToString() + "] in SetZoneSource call.");
			}
		}

		#endregion IMatrixSwitcherDriver Members

		#region IMultiroomAudioDriver Members

		[ScriptObjectMethod("Decrement Zone Volume", "Decrement the volume on a zone.", "Decrement the volume for zone {PARAM|0|1} on {NAME}.")]
		[ScriptObjectMethodParameter("ZoneNumber", "The number of the zone.", MIN_ZONE_NUMBER, MAX_ZONE_NUMBER, "ZoneNames")]
		public void DecrementZoneVolume(ScriptNumber zoneNumber)
		{
			int zoneIndex = zoneNumber.ToPrimitiveInt32() - 1;
			if (zoneIndex < this._zones.Length)
			{
				Zone zone = this._zones[zoneIndex];
				zone.DecrementVolume();
			}
			else
			{
				this.Logger.Error("Invalid ZoneNumber [" + zoneNumber.ToString() + "] in DecrementZoneVolume call.");
			}
		}

		[ScriptObjectMethod("Increment Zone Volume", "Increment the volume on a zone.", "Increment the volume for zone {PARAM|0|1} on {NAME}.")]
		[ScriptObjectMethodParameter("ZoneNumber", "The number of the zone.", MIN_ZONE_NUMBER, MAX_ZONE_NUMBER, "ZoneNames")]
		public void IncrementZoneVolume(ScriptNumber zoneNumber)
		{
			int zoneIndex = zoneNumber.ToPrimitiveInt32() - 1;
			if (zoneIndex < this._zones.Length)
			{
				Zone zone = this._zones[zoneIndex];
				zone.IncrementVolume();
			}
			else
			{
				this.Logger.Error("Invalid ZoneNumber [" + zoneNumber.ToString() + "] in IncrementZoneVolume call.");
			}
		}

		[ScriptObjectMethod("Mute Zone", "Mute a zone.", "Mute zone {PARAM|0|1} on {NAME}.")]
		[ScriptObjectMethodParameter("ZoneNumber", "The number of the zone.", MIN_ZONE_NUMBER, MAX_ZONE_NUMBER, "ZoneNames")]
		public void MuteZone(ScriptNumber zoneNumber)
		{
			int zoneIndex = zoneNumber.ToPrimitiveInt32() - 1;
			if (zoneIndex < this._zones.Length)
			{
				this._zones[zoneIndex].SetMuteState(true);
			}
			else
			{
				this.Logger.Error("Invalid ZoneNumber [" + zoneNumber.ToString() + "] in MuteZone call.");
			}
		}

		[ScriptObjectMethod("Set Zone Volume", "Set the volume on a zone.", "Set the volume to {PARAM|1|10} for zone {PARAM|0|1} on {NAME}.")]
		[ScriptObjectMethodParameter("ZoneNumber", "The number of the zone.", MIN_ZONE_NUMBER, MAX_ZONE_NUMBER, "ZoneNames")]
		[ScriptObjectMethodParameter("Volume", "The volume level.", MIN_VOLUME, MAX_VOLUME)]
		public void SetZoneVolume(ScriptNumber zoneNumber, ScriptNumber volume)
		{
			int zoneIndex = zoneNumber.ToPrimitiveInt32() - 1;
			if (zoneIndex < this._zones.Length)
			{
				Zone zone = this._zones[zoneIndex];
				zone.SetVolume(volume.ToPrimitiveInt32());
			}
			else
			{
				this.Logger.Error("Invalid ZoneNumber [" + zoneNumber.ToString() + "] in SetZoneVolume call.");
			}
		}

		[ScriptObjectMethod("Toggle Zone Mute", "Toggle mute on a zone.", "Toggle mute for zone {PARAM|0|1} on {NAME} .")]
		[ScriptObjectMethodParameter("ZoneNumber", "The number of the zone.", MIN_ZONE_NUMBER, MAX_ZONE_NUMBER, "ZoneNames")]
		public void ToggleZoneMute(ScriptNumber zoneNumber)
		{
			int zoneIndex = zoneNumber.ToPrimitiveInt32() - 1;
			if (zoneIndex < this._zones.Length)
			{
				this._zones[zoneIndex].ToggleMuteState();
			}
			else
			{
				this.Logger.Error("Invalid ZoneNumber [" + zoneNumber.ToString() + "] in ToggleZoneMute call.");
			}
		}

		[ScriptObjectMethod("Toggle Zone Power", "Toggle power on a zone.", "Toggle power for zone {PARAM|0|1} on {NAME}.")]
		[ScriptObjectMethodParameter("ZoneNumber", "The number of the zone.", MIN_ZONE_NUMBER, MAX_ZONE_NUMBER, "ZoneNames")]
		public void ToggleZonePower(ScriptNumber zoneNumber)
		{
			int zoneIndex = zoneNumber.ToPrimitiveInt32() - 1;
			if (zoneIndex < this._zones.Length)
			{
				this._zones[zoneIndex].TogglePowerState();
			}
			else
			{
				this.Logger.Error("Invalid ZoneNumber [" + zoneNumber.ToString() + "] in ToggleZonePower call.");
			}
		}

		[ScriptObjectMethod("Turn All Zones Off", "Turn the power off on all zones.", "Turn power off for all zones on {NAME}.")]
		public void TurnAllZonesOff()
		{
			this._comm.Send("!AO+");
		}

		[ScriptObjectMethod("Turn Zone Off", "Turn a zone off.", "Turn power off for zone {PARAM|0|1} on {NAME}.")]
		[ScriptObjectMethodParameter("ZoneNumber", "The number of the zone.", MIN_ZONE_NUMBER, MAX_ZONE_NUMBER, "ZoneNames")]
		public void TurnZoneOff(ScriptNumber zoneNumber)
		{
			int zoneIndex = zoneNumber.ToPrimitiveInt32() - 1;
			if (zoneIndex < this._zones.Length)
			{
				this._zones[zoneIndex].SetPowerState(false);
			}
			else
			{
				this.Logger.Error("Invalid ZoneNumber [" + zoneNumber.ToString() + "] in TurnZoneOff call.");
			}
		}

		[ScriptObjectMethod("Turn Zone On", "Turn a zone on.", "Turn power on for zone {PARAM|0|1} on {NAME}.")]
		[ScriptObjectMethodParameter("ZoneNumber", "The number of the zone.", MIN_ZONE_NUMBER, MAX_ZONE_NUMBER, "ZoneNames")]
		public void TurnZoneOn(ScriptNumber zoneNumber)
		{
			int zoneIndex = zoneNumber.ToPrimitiveInt32() - 1;
			if (zoneIndex < this._zones.Length)
			{
				this._zones[zoneIndex].SetPowerState(true);
			}
			else
			{
				this.Logger.Error("Invalid ZoneNumber [" + zoneNumber.ToString() + "] in TurnZoneOn call.");
			}
		}

		[ScriptObjectMethod("Unmute Zone", "Unmute a zone.", "Unmute zone {PARAM|0|1} on {NAME}.")]
		[ScriptObjectMethodParameter("ZoneNumber", "The number of the zone.", MIN_ZONE_NUMBER, MAX_ZONE_NUMBER, "ZoneNames")]
		public void UnmuteZone(ScriptNumber zoneNumber)
		{
			int zoneIndex = zoneNumber.ToPrimitiveInt32() - 1;
			if (zoneIndex < this._zones.Length)
			{
				this._zones[zoneIndex].SetMuteState(false);
			}
			else
			{
				this.Logger.Error("Invalid ZoneNumber [" + zoneNumber.ToString() + "] in UnmuteZone call.");
			}
		}

		#endregion IMultiroomAudioDriver Members

		[ScriptObjectMethod("Set Zone Balance", "Set the balance on a zone.", "Set the balance to {PARAM|1|0} for zone {PARAM|0|1} on {NAME}.")]
		[ScriptObjectMethodParameter("ZoneNumber", "The number of the zone.", MIN_ZONE_NUMBER, MAX_ZONE_NUMBER, "ZoneNames")]
		[ScriptObjectMethodParameter("Balance", "The balance (negative = left, positive = right).", MIN_BALANCE, MAX_BALANCE)]
		public void SetZoneBalance(ScriptNumber zoneNumber, ScriptNumber balance)
		{
			int zoneIndex = zoneNumber.ToPrimitiveInt32() - 1;
			if (zoneIndex < this._zones.Length)
			{
				this._zones[zoneIndex].SetBalance(balance.ToPrimitiveInt32());
			}
			else
			{
				this.Logger.Error("Invalid ZoneNumber [" + zoneNumber.ToString() + "] in SetZoneBalance call.");
			}
		}

		[ScriptObjectMethod("Set Zone Mute State", "Set the mute state on a zone.", "Set the mute state to {PARAM|1|false|Muted|Unmuted} for zone {PARAM|0|1} on {NAME}.")]
		[ScriptObjectMethodParameter("ZoneNumber", "The number of the zone.", MIN_ZONE_NUMBER, MAX_ZONE_NUMBER, "ZoneNames")]
		[ScriptObjectMethodParameter("IsMuteOn", "Whether to enable mute.")]
		protected void SetZoneMuteState(ScriptNumber zoneNumber, ScriptBoolean isMuteOn)
		{
			if (isMuteOn)
			{
				this.MuteZone(zoneNumber);
			}
			else
			{
				this.UnmuteZone(zoneNumber);
			}
		}

		[ScriptObjectMethod("Set Zone Power State", "Set the power state on a zone.", "Set the power state to {PARAM|1|true|On|Off} for zone {PARAM|0|1} on {NAME}.")]
		[ScriptObjectMethodParameter("ZoneNumber", "The number of the zone.", MIN_ZONE_NUMBER, MAX_ZONE_NUMBER, "ZoneNames")]
		[ScriptObjectMethodParameter("IsPowerOn", "Whether power is on.")]
		protected void SetZonePowerState(ScriptNumber zoneNumber, ScriptBoolean isPowerOn)
		{
			if (isPowerOn)
			{
				this.TurnZoneOn(zoneNumber);
			}
			else
			{
				this.TurnZoneOff(zoneNumber);
			}
		}

		[ScriptObjectMethod("Set Zone Source By Name", "Set the source on a zone.", "Set the source to {PARAM|1|} for zone {PARAM|0|1} on {NAME}.")]
		[ScriptObjectMethodParameter("ZoneNumber", "The number of the zone.", MIN_ZONE_NUMBER, MAX_ZONE_NUMBER, "ZoneNames")]
		[ScriptObjectMethodParameter("SourceName", "The name of the source to select.")]
		protected void SetZoneSourceByName(ScriptNumber zoneNumber, ScriptString sourceName)
		{
			int zoneIndex = zoneNumber.ToPrimitiveInt32() - 1;
			if (zoneIndex < this._zones.Length)
			{
				if (this._sourceNames.ToList().Any(s => s == sourceName))
				{
					this._zones[zoneIndex].SetSource(this._sourceNames.ToList().IndexOf("sourceName"));
				}
				else
				{
					this.Logger.Error("Invalid SourceName [" + sourceName + "] in SetZoneSourceByName call.");
				}
			}
			else
			{
				this.Logger.Error("Invalid ZoneNumber [" + zoneNumber.ToString() + "] in SetZoneSourceByName call.");
			}
		}

		#endregion ScriptObjectMethods

		public override bool StartDriver(Dictionary<string, byte[]> configFileData)
		{
			this.Logger.Debug("Xantech DIGI-5 Driver Starting");

			this._comm = new SerialCommunication(this.SerialPortSetting, 9600, System.IO.Ports.Parity.None, 8, System.IO.Ports.StopBits.One);

			this._zones = new Zone[this.ZoneCountSetting];

			this.Logger.Debug("Creating [" + this.ZoneCountSetting + "] Zones.");

			// Create Zones
			for (int i = 0; i < this.ZoneCountSetting; i++)
			{
				Zone zone = new Zone(this._comm, i + 1, this.Logger);
				zone.BalanceChanged += new EventHandler(ZoneBalanceChanged);
				zone.BassLevelChanged += new EventHandler(ZoneBassLevelChanged);
				zone.MuteStateChanged += new EventHandler(ZoneMuteStateChanged);
				zone.PowerStateChanged += new EventHandler(ZonePowerStateChanged);
				zone.SourceChanged += new EventHandler(ZoneSourceChanged);
				zone.TrebleLevelChanged += new EventHandler(ZoneTrebleLevelChanged);
				zone.VolumeChanged += new EventHandler(ZoneVolumeChanged);
				this._zones[i] = zone;
			}

			this.Logger.Debug("Zones Created.");

			this._comm.ConnectionEstablished += new EventHandler<EventArgs>(ConnectionEstablished);
			this._comm.ConnectionLost += new EventHandler<EventArgs>(ConnectionLost);
			this._comm.ConnectionMonitorTimeout = 1000;
			this._comm.ConnectionMonitorTestRequest = "?DI+";
			this._comm.CurrentEncoding = Encoding.ASCII;
			this._comm.Delimiter = "+";
			this._comm.Logger = this.Logger;
			this._comm.ReadBufferEnabled = true;
			this._comm.ReceivedDelimitedString += new EventHandler<ReceivedDelimitedStringEventArgs>(ReceivedDelimitedString);
			this._comm.Open();
			this._comm.StartConnectionMonitor();

			this._updateStatusTimer = new System.Timers.Timer();
			this._updateStatusTimer.Interval = this.RefreshIntervalSetting * 1000;
			this._updateStatusTimer.AutoReset = false;
			this._updateStatusTimer.Elapsed += new ElapsedEventHandler(this.UpdateStatus);
			this._updateStatusTimer.Start();

			this.Logger.Debug("Xantech DIGI-5 Driver Started");

			return true;
		}

		public override void StopDriver()
		{
			if (this._updateStatusTimer != null)
			{
				this._updateStatusTimer.Stop();
				this._updateStatusTimer.Dispose();
				this._updateStatusTimer = (System.Timers.Timer)null;
			}

			if (this._comm != null)
			{
				this._comm.StopConnectionMonitor();
				this._comm.Dispose();
			}

			this.Logger.Debug("Xantech DIGI-5 Driver Stopped");
		}

		protected void ConnectionEstablished(object sender, EventArgs e)
		{
			this.Logger.Debug("Xantech DIGI-5 Connection Established.");
		}

		protected void ConnectionLost(object sender, EventArgs e)
		{
			this.Logger.Warning("Xantech DIGI-5 Connection Lost.");
		}

		protected void ReceivedDelimitedString(object sender, ReceivedDelimitedStringEventArgs e)
		{
			if (e.RawResponse.StartsWith("?S"))
			{
				this.Logger.Debug("Device handling response [" + e.RawResponse + "].");

				this.LastUpdated = new ScriptDateTime(DateTime.Now);

				string keyword;
				int startIndex, endIndex;

				keyword = "Device: ";
				if (e.RawResponse.Contains(keyword))
				{
					startIndex = e.RawResponse.IndexOf(keyword) + keyword.Length;
					endIndex = e.RawResponse.IndexOf(",", startIndex);
					string value = e.RawResponse.Substring(startIndex, endIndex - startIndex);
					this.Logger.Debug("Parsed value [" + value + "] for property [" + keyword + "]");
					this.DeviceType = new ScriptString(value);
				}

				keyword = "Device Cde: ";
				if (e.RawResponse.Contains(keyword))
				{
					startIndex = e.RawResponse.IndexOf(keyword) + keyword.Length;
					endIndex = e.RawResponse.IndexOf(",", startIndex);
					string value = e.RawResponse.Substring(startIndex, endIndex - startIndex);
					this.Logger.Debug("Parsed value [" + value + "] for property [" + keyword + "]");
					this.DeviceCode = new ScriptString(value);
				}

				keyword = "Hardware Cde: ";
				if (e.RawResponse.Contains(keyword))
				{
					startIndex = e.RawResponse.IndexOf(keyword) + keyword.Length;
					endIndex = e.RawResponse.IndexOf(",", startIndex);
					string value = e.RawResponse.Substring(startIndex, endIndex - startIndex);
					this.Logger.Debug("Parsed value [" + value + "] for property [" + keyword + "]");
					this.HardwareCode = new ScriptString(value);
				}

				keyword = "Firmware Ver: ";
				if (e.RawResponse.Contains(keyword))
				{
					startIndex = e.RawResponse.IndexOf(keyword) + keyword.Length;
					endIndex = e.RawResponse.Length;
					string value = e.RawResponse.Substring(startIndex, endIndex - startIndex);
					this.Logger.Debug("Parsed value [" + value + "] for property [" + keyword + "]");
					this.FirmwareVersion = new ScriptString(value);
				}
			}
		}

		protected void UpdateStatus(object sender, ElapsedEventArgs e)
		{
			try
			{
				this._updateStatusTimer.Stop();

				this.Logger.Debug("Updating Device Status.");

				this._comm.Send("?SI+");

				this.Logger.Debug("Device Status Query Sent.");

				foreach (Zone zone in this._zones)
				{
					if (zone != null)
					{
						zone.UpdateStatus();
					}
				}

				this.Logger.Debug("Finished Updating Zones.");
			}
			catch (Exception ex)
			{
				this.Logger.Error("Error updating Xantech DIGI-5 Status.", ex);
			}
			finally { this._updateStatusTimer.Start(); }
		}

		protected void ZoneBalanceChanged(object sender, EventArgs e)
		{
			Zone zone = sender as Zone;
			DevicePropertyChangeNotification("ZoneBalances", zone.Number, this.ZoneBalances[zone.Number]);
		}

		protected void ZoneBassLevelChanged(object sender, EventArgs e)
		{
			Zone zone = sender as Zone;
			DevicePropertyChangeNotification("ZoneBassLevels", zone.Number, this.ZoneBassLevels[zone.Number]);
		}

		protected void ZoneMuteStateChanged(object sender, EventArgs e)
		{
			Zone zone = sender as Zone;
			DevicePropertyChangeNotification("ZoneMuteStates", zone.Number, this.ZoneMuteStates[zone.Number]);
		}

		protected void ZonePowerStateChanged(object sender, EventArgs e)
		{
			Zone zone = sender as Zone;
			DevicePropertyChangeNotification("ZonePowerStates", zone.Number, this.ZonePowerStates[zone.Number]);
		}

		protected void ZoneSourceChanged(object sender, EventArgs e)
		{
			Zone zone = sender as Zone;
			DevicePropertyChangeNotification("ZoneSources", zone.Number, this.ZoneSources[zone.Number]);
			DevicePropertyChangeNotification("ZoneSourceNames", zone.Number, this.ZoneSourceNames[zone.Number]);
		}

		protected void ZoneTrebleLevelChanged(object sender, EventArgs e)
		{
			Zone zone = sender as Zone;
			DevicePropertyChangeNotification("ZoneTrebleLevels", zone.Number, this.ZoneTrebleLevels[zone.Number]);
		}

		protected void ZoneVolumeChanged(object sender, EventArgs e)
		{
			Zone zone = sender as Zone;
			DevicePropertyChangeNotification("ZoneVolumes", zone.Number, this.ZoneVolumes[zone.Number]);
		}

		#endregion Methods
	}
}