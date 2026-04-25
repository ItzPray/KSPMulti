using LmpClient.Localization;
using LmpClient.Utilities;
using LmpCommon;
using LmpGlobal;
using System;
using System.Collections;
using UnityEngine;

namespace LmpClient.Windows.Update
{
    public partial class UpdateWindow
    {
        private static string _installStatus;
        private static bool _installing;
        private static GUIStyle _statusWordWrap;

        private static GUIStyle InstallStatusStyle
        {
            get
            {
                if (_statusWordWrap == null)
                {
                    _statusWordWrap = new GUIStyle(GUI.skin.label)
                    {
                        wordWrap = true,
                        fontSize = 12,
                        normal = { textColor = Color.white }
                    };
                }
                return _statusWordWrap;
            }
        }

        private IEnumerator CoDownloadAndInstallFromRelease()
        {
            var url = ClientZipDownloadUrl;
            if (string.IsNullOrEmpty(url) || _installing) yield break;
            _installing = true;
            _installStatus = null;
            if (MainSystem.Singleton == null) { _installing = false; yield break; }

            var failed = (Exception)null;
            yield return MainSystem.Singleton.StartCoroutine(ClientModUpdateInstaller.CoDownloadAndInstallKspmpFolder(url,
                s => { _installStatus = s; },
                userMsg =>
                {
                    _installStatus = userMsg;
                    LunaScreenMsg.PostScreenMessage(userMsg, 12f, ScreenMessageStyle.UPPER_LEFT, Color.cyan);
                },
                ex => { failed = ex; }));

            _installing = false;
            if (failed != null)
            {
                LunaLog.LogError("[KSPMP] Client update: " + failed);
                _installStatus = string.Format(LocalizationContainer.UpdateWindowText.InstallFailed, failed.Message);
            }
        }

        public static void ClearInstallState()
        {
            _installStatus = null;
            _installing = false;
        }

        protected override void DrawWindowContent(int windowId)
        {
            GUILayout.BeginVertical();
            GUI.DragWindow(MoveRect);

            GUILayout.Label(LocalizationContainer.UpdateWindowText.Text, LmpVersioning.IsCompatible(LatestVersion) ? BoldGreenLabelStyle : BoldRedLabelStyle);

            GUILayout.BeginVertical();
            GUILayout.Label(LocalizationContainer.UpdateWindowText.CurrentVersion + " " + LmpVersioning.CurrentVersion);
            GUILayout.Label(LocalizationContainer.UpdateWindowText.LatestVersion + " " + LatestVersion);
            GUILayout.EndVertical();

            GUILayout.BeginVertical();
            if (LmpVersioning.IsCompatible(LatestVersion))
                GUILayout.Label(LocalizationContainer.UpdateWindowText.StillCompatible, BoldGreenLabelStyle);
            else
                GUILayout.Label(LocalizationContainer.UpdateWindowText.NotCompatible, BoldRedLabelStyle);
            GUILayout.EndVertical();

            GUILayout.Label(LocalizationContainer.UpdateWindowText.Changelog);

            GUILayout.BeginVertical();
            var scrollH = _installing || !string.IsNullOrEmpty(_installStatus) ? 85f : 100f;
            ScrollPos = GUILayout.BeginScrollView(ScrollPos, GUILayout.Width(WindowWidth - 5), GUILayout.Height(WindowHeight - scrollH));
            GUILayout.Label(Changelog);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            if (!string.IsNullOrEmpty(_installStatus))
            {
                GUILayout.Label(_installStatus, InstallStatusStyle, GUILayout.MaxWidth(WindowWidth - 10f));
            }

            var prev = GUI.enabled;
            GUI.enabled = !(_installing);

            if (!string.IsNullOrEmpty(ClientZipDownloadUrl))
            {
                if (GUILayout.Button(LocalizationContainer.UpdateWindowText.DownloadAndInstallKspmp) && MainSystem.Singleton != null)
                {
                    var isWin = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;
                    if (isWin)
                    {
                        _installing = true;
                        KspmpWindowsUpdateLauncher.TryStartExternalUpdate(ClientZipDownloadUrl, out var okMsg, out var err);
                        if (!string.IsNullOrEmpty(err))
                        {
                            _installing = false;
                            _installStatus = err;
                            LunaLog.LogError("[KSPMP] External update: " + err);
                        }
                        else if (!string.IsNullOrEmpty(okMsg))
                        {
                            _installStatus = okMsg;
                            LunaScreenMsg.PostScreenMessage(okMsg, 16f, ScreenMessageStyle.UPPER_LEFT, Color.cyan);
                        }
                    }
                    else
                    {
                        var ut = LocalizationContainer.UpdateWindowText;
                        _installStatus = string.Format(ut.InstallWorking, ut.InstallPhaseDownloading);
                        MainSystem.Singleton.StartCoroutine(CoDownloadAndInstallFromRelease());
                    }
                }
                if (GUILayout.Button(LocalizationContainer.UpdateWindowText.OpenReleasesInBrowser))
                {
                    Application.OpenURL(RepoConstants.LatestGithubReleaseUrl);
                }
            }
            else
            {
                if (GUILayout.Button(LocalizationContainer.UpdateWindowText.OpenReleasesInBrowser))
                {
                    Application.OpenURL(RepoConstants.LatestGithubReleaseUrl);
                    Display = false;
                }
            }

            GUI.enabled = prev;
            GUILayout.EndVertical();
        }
    }
}
