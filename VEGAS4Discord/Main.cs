using System;
using System.Windows.Forms;
using System.Collections;
using ScriptPortal.Vegas;
using DiscordRpcNet;
using System.Linq;
using VegasDiscordRPC.Forms;

namespace VegasDiscordRPC
{

    public class EntryPoint : ICustomCommandModule
    {
        // DummyDocker is here because using a Docker is the only way I can listen to the VEGAS window closing.
        DockableControl dummyDocker;
        DiscordRpc.EventHandlers callbacks = new DiscordRpc.EventHandlers();
        DiscordRpc.RichPresence presence = new DiscordRpc.RichPresence();
        public LogFile lfile = new LogFile();
        bool PresenceEnabled { get; set; } = true;
        Timer idleTimer = new Timer();
        int SecondsSinceLastAction;
        // Whether it's playing, rendering...
        bool isActive;

        private long unixTimestamp(DateTime dt)
        {
            return (dt.Ticks - 621355968000000000) / 10000000;
        }

        public ICollection GetCustomCommands()
        {
            CustomCommand cmd = new CustomCommand(CommandCategory.Tools, "ToggleStatus")
            {
                DisplayName = "Toggle Rich Presence"
            };
            cmd.Invoked += (a, b) => TogglePresence(DomainManager.VegasDomainManager.GetVegas());
            callbacks.disconnectedCallback += (a, b) => DisconnectedCallback(a, b);
            callbacks.errorCallback += (a, b) => ErrorCallback(a, b);
            callbacks.readyCallback += () => ReadyCallback();

            dummyDocker = new DockableControl("dummyDocker");
            return new CustomCommand[] { cmd };
        }

        void loadDummyDocker(Vegas vegas)
        {
            // because VEGAS API is stupid and only allows to listen on app close through a docked control
            // so we make a super tiny one where nobody can see it and we win
            dummyDocker.AppWindowClosed += (a, b) => { Shutdown(); };
            dummyDocker.DefaultDockWindowStyle = DockWindowStyle.Detached;
            dummyDocker.DefaultFloatingSize = new System.Drawing.Size(1, 1);
            dummyDocker.DefaultFloatingLocation = new System.Drawing.Point(2, 2);
            vegas.LoadDockView(dummyDocker);
        }

        public void Shutdown()
        {
            dummyDocker.Dispose();
            DiscordRpc.Shutdown();
        }

        private long startTime;

        public void InitializeModule(Vegas vegas)
        {
            resetPresence(ref presence, vegas);
            DiscordRpc.Initialize("1088784098564259970", ref callbacks, true, string.Empty);

            startTime = unixTimestamp(DateTime.UtcNow);
            presence.startTimestamp = startTime;

            presence.state = "Idling";
            vegas.TrackCountChanged += (a, b) => UpdateDetails(vegas);
            vegas.TrackEventCountChanged += (a, b) => UpdateDetails(vegas);
            vegas.PlaybackStarted += (a, b) => { isActive = true; GenericNonIdleAction(vegas); };
            vegas.PlaybackStopped += (a, b) => { isActive = false; GenericNonIdleAction(vegas); };
            vegas.ProjectSaving += (a, b) => GenericNonIdleAction(vegas);
            vegas.RenderStarted += (a, b) => RenderStarted(vegas);
            vegas.RenderProgress += (a, b) => RenderEvtProgress(b, vegas);
            vegas.RenderFinished += (a, b) => RenderEvtFinish(b, vegas);
            vegas.AppInitialized += (a, b) => loadDummyDocker((Vegas)a);
            DiscordRpc.RunCallbacks();
            DiscordRpc.UpdatePresence(ref presence);
            idleTimer.Interval = 10000;
            idleTimer.Tick += (a, b) => IntervalTick();
            idleTimer.Start();
        }

        public void IntervalTick()
        {
            if (SecondsSinceLastAction < 60 && !isActive)
            {
                SecondsSinceLastAction += 10;
            }
            else if (SecondsSinceLastAction >= 300 && !isActive)
            {
                resetPresence(ref presence, DomainManager.VegasDomainManager.GetVegas());
                presence.state = "Idling";
                DiscordRpc.UpdatePresence(ref presence);
            }
        }

        /// <summary>
        /// Resets the presence object.
        /// </summary>
        /// <param name="pres">The presence object to reset (Future use: ?)</param>
        /// <param name="vegas">The Vegas object</param>
        /// <param name="smallKey">Small image key</param>
        /// <param name="smallText">Small image hover text</param>
        public void resetPresence(ref DiscordRpc.RichPresence pres, Vegas vegas, string smallKey = "", string smallText = "")
        {
            if (!PresenceEnabled)
                return;

            int version = int.Parse(vegas.Version.Split(' ')[1].Split('.')[0]);
            String largeKey = "vegas_" + version + "_logo";

            pres = new DiscordRpc.RichPresence
            {
                largeImageKey = largeKey,
                largeImageText = vegas.Version,
            };
            if (smallKey != "")
            {
                pres.smallImageKey = smallKey;
            }
            if (smallText != "")
            {
                pres.smallImageText = smallText;
            }

            pres.startTimestamp = startTime;
            presence.details = GetProjectName(vegas);
        }

        public void RenderStarted(Vegas vegas)
        {
            if (!PresenceEnabled)
                return;

            SecondsSinceLastAction = 0;
            isActive = true;
            resetPresence(ref presence, vegas);
            presence.state = "Rendering... (0%)";
            DiscordRpc.UpdatePresence(ref presence);
        }

        public void RenderEvtProgress(RenderStatusEventArgs renderargs, Vegas vegas)
        {
            if (!PresenceEnabled)
                return;

            presence.state = $"Rendering... ({renderargs.PercentComplete}%)";
            DiscordRpc.UpdatePresence(ref presence);
        }


        public void RenderEvtFinish(RenderStatusEventArgs renderargs, Vegas vegas)
        {
            if (!PresenceEnabled)
                return;

            SecondsSinceLastAction = 0;
            isActive = false;
            presence = new DiscordRpc.RichPresence();
            resetPresence(ref presence, vegas);

            switch (renderargs.Status)
            {
                case RenderStatus.Complete:
                    presence.state = "Just finished rendering";
                    break;
                case RenderStatus.Failed:
                    presence.state = "Just failed to render";
                    break;
                case RenderStatus.Canceled:
                    presence.state = "Just canceled rendering";
                    break;
            }
            DiscordRpc.UpdatePresence(ref presence);
        }

        public void GenericNonIdleAction(Vegas vegas)
        {
            if (!PresenceEnabled)
                return;
            SecondsSinceLastAction = 0;
            UpdateDetails(vegas);
        }

        public void UpdateDetails(Vegas vegas)
        {
            if (!PresenceEnabled)
                return;

            SecondsSinceLastAction = 0;
            resetPresence(ref presence, vegas);

            presence.state = "Editing";
            DiscordRpc.UpdatePresence(ref presence);
        }

        public String GetProjectName(Vegas vegas)
        {
            return (vegas.Project.IsUntitled ? "Untitled" : vegas.Project.FilePath.Split('\\').Last());
        }

        public void TogglePresence(Vegas vegas)
        {
            if (PresenceEnabled)
            {
                RichPresenceToggle rpct = new RichPresenceToggle(false);
                rpct.ShowDialog(vegas.MainWindow);
                idleTimer.Stop();
                PresenceEnabled = false;
                DiscordRpc.Shutdown();
            }
            else
            {
                RichPresenceToggle rpct = new RichPresenceToggle(true);
                rpct.ShowDialog(vegas.MainWindow);
                idleTimer.Start();
                PresenceEnabled = true;
                resetPresence(ref presence, vegas);
                DiscordRpc.Initialize("1088784098564259970", ref callbacks, true, string.Empty);
                UpdateDetails(vegas);
            }
        }

        void ReadyCallback()
        {
            lfile.AddLogEntry(DateTime.Now.ToLongTimeString() + " - Discord's ready");
            lfile.Close();
        }

        void DisconnectedCallback(int errorCode, string message)
        {
            lfile.AddLogEntry(DateTime.Now.ToLongTimeString() + " - Discord Disconnected! (" + errorCode + "-" + message + ")");
            lfile.Close();
        }

        void ErrorCallback(int errorCode, string message)
        {
            lfile.AddLogEntry(DateTime.Now.ToLongTimeString() + " - Discord Error! (" + errorCode + "-" + message + ")");
            lfile.Close();
        }
    }
}
