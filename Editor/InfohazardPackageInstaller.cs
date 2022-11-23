// The MIT License (MIT)
// 
// Copyright (c) 2022-present Vincent Miller
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

// Please feel free to copy and modify this file in order to make your own installer!
// This file was not created with good design in mind, it is just an easy way to get my packages into your project.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Infohazard.PackageInstaller.Editor {
    public class ManagedPackageInfo {
        public string DisplayName { get; set; }
        public string PackageIdentifier { get; set; }
        public string RepoName { get; set; }
        public string GitUrl { get; set; }
        public string GitZipUrl { get; set; }
        public string Description { get; set; }
        public string[] Dependencies { get; set; }

        public static readonly ManagedPackageInfo[] Packages = {
            new ManagedPackageInfo {
                DisplayName = "Core",
                PackageIdentifier = "com.infohazard.core",
                RepoName = "Infohazard.Core",
                GitUrl = "https://github.com/vcmiller/Infohazard.Core.git",
                GitZipUrl = "https://api.github.com/repos/vcmiller/Infohazard.Core/zipball",
                Description = "A collection of useful functionality for Unity, which is used by my other packages.",
                Dependencies = Array.Empty<string>(),
            },
            new ManagedPackageInfo {
                DisplayName = "Sequencing",
                PackageIdentifier = "com.infohazard.sequencing",
                RepoName = "Infohazard.Sequencing",
                GitUrl = "https://github.com/vcmiller/Infohazard.Sequencing.git",
                GitZipUrl = "https://api.github.com/repos/vcmiller/Infohazard.Sequencing/zipball",
                Description = "A system for creating startup and level loading sequences, as well as saving game state.",
                Dependencies = new[]{ "com.infohazard.core" }
            },
            new ManagedPackageInfo {
                DisplayName = "State System",
                PackageIdentifier = "com.infohazard.statesystem",
                RepoName = "Infohazard.StateSystem",
                GitUrl = "https://github.com/vcmiller/Infohazard.StateSystem.git",
                GitZipUrl = "https://api.github.com/repos/vcmiller/Infohazard.StateSystem/zipball",
                Description = "A system for creating and applying states which override a component's values.",
                Dependencies = new[] { "com.infohazard.core" }
            },
            new ManagedPackageInfo {
                DisplayName = "HyperNav",
                PackageIdentifier = "com.infohazard.hypernav",
                RepoName = "Infohazard.HyperNav",
                GitUrl = "https://github.com/vcmiller/Infohazard.HyperNav.git",
                GitZipUrl = "https://api.github.com/repos/vcmiller/Infohazard.HyperNav/zipball",
                Description = "A 3D volume-based navigation system.",
                Dependencies = new[]{ "com.infohazard.core" }
            },
            new ManagedPackageInfo {
                DisplayName = "Slightly Better Rats",
                PackageIdentifier = "com.infohazard.slightlybetterrats",
                RepoName = "SlightlyBetterRats",
                GitUrl = "https://github.com/vcmiller/SlightlyBetterRats.git",
                GitZipUrl = "https://api.github.com/repos/vcmiller/SlightlyBetterRats/zipball",
                Description = "Various Unity utilities that are not general enough or not of sufficient code quality to be in their own packages.",
                Dependencies = new[] {
                    "com.infohazard.core",
                    "com.infohazard.sequencing",
                    "com.infohazard.statesystem",
                },
            }
        };

        public static ManagedPackageInfo GetPackageInfo(string identifier) =>
            Packages.FirstOrDefault(pkg => pkg.PackageIdentifier == identifier);

        public static void GetPackageDependencies(string identifier, HashSet<string> set) {
            ManagedPackageInfo pkg = GetPackageInfo(identifier);
            if (pkg == null) {
                Debug.LogError($"Package not found: {identifier}.");
                return;
            }

            set.Add(identifier);
            foreach (string dependency in pkg.Dependencies) {
                GetPackageDependencies(dependency, set);
            }
        }
    }
    
    public class InfohazardPackageInstallerWindow : EditorWindow {
        private const int OptionNone = 0;
        private const int OptionGitURL = 1;
        private const int OptionEmbedded = 2;
        private const int OptionSubmodule = 3;
        
        private GUIStyle _headerLabelStyle;
        private GUIStyle _statusStyle;
        private ReorderableList _list;
        private bool _canUseSubmodule;
        private int _selectedIndex = -1;
        private HashSet<string> _selectedDependencies;
        private bool _dependenciesFoldout;
        private string[] _installOptions;
        private string[] _installOptionsRequired;
        private Dictionary<string, InstalledManagedPackageInfo> _installedPackages;

        private IEnumerator _currentAction;

        private string _gitModulesPath;

        [MenuItem("Tools/Infohazard/Installer")]
        public static void ShowWindow() {
            var window = GetWindow<InfohazardPackageInstallerWindow>();
            window.titleContent = new GUIContent("Package Installer");
            window.Show();
        }

        [InitializeOnLoadMethod]
        public static void Prompt() {
            if (EditorPrefs.GetBool("Infohazard.Installer.Prompt", false)) return;
            EditorPrefs.SetBool("Infohazard.Installer.Prompt", true);
            
            if (EditorUtility.DisplayDialog("Infohazard Package Installer",
                    "Do you want to open the Infohazard Package Installer window?", "Yes", "No")) {
                ShowWindow();
            }
        }

        private static bool ExecuteProcess(string command, string args, bool showMessages) {
            ProcessStartInfo processInfo = new ProcessStartInfo(command, args) {
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            Process process = Process.Start(processInfo);
            if (process != null) {
                process.WaitForExit();
                bool success = process.ExitCode == 0;
                if (!success && showMessages) {
                    EditorUtility.DisplayDialog("Process returned failure", process.StandardError.ReadToEnd(), "OK");
                }
                process.Close();
                return success;
            } else {
                if (showMessages) {
                    EditorUtility.DisplayDialog("Process failed ot start", processInfo.ToString(), "OK");
                }
                return false;
            }
        }

        private static bool CheckGitStatus() {
            return ExecuteProcess("git", "status", false);
        }

        private void OnEnable() {
            _list = new ReorderableList(ManagedPackageInfo.Packages, typeof(ManagedPackageInfo), false, false, false, false);
            _list.headerHeight = EditorGUIUtility.singleLineHeight;
            _list.drawElementCallback = DrawElementCallback;
            _list.multiSelect = false;
            Refresh();

            EditorApplication.update += EditorApplication_Update;
        }

        private void Refresh() {
            _canUseSubmodule = CheckGitStatus();
            _gitModulesPath = Path.Combine(Application.dataPath, "..", ".gitmodules");
            
            List<string> options = new List<string> {
                "Not Installed",
                "Git URL",
                "Embedded",
            };
            if (_canUseSubmodule) options.Add("Git Submodule");
            _installOptions = options.ToArray();
            options.RemoveAt(0);
            _installOptionsRequired = options.ToArray();
            _currentAction = RefreshPackages();
        }

        private void OnDisable() {
            EditorApplication.update -= EditorApplication_Update;
        }

        private void EditorApplication_Update() {
            if (_currentAction != null && !_currentAction.MoveNext()) {
                _currentAction = null;
            }
        }

        private void OnGUI() {
            if (_headerLabelStyle == null) {
                _headerLabelStyle = new GUIStyle(EditorStyles.largeLabel);
                _headerLabelStyle.fontStyle = FontStyle.Bold;
                _headerLabelStyle.fontSize = 18;
                _headerLabelStyle.margin.top = 10;
                _headerLabelStyle.margin.bottom = 10;
                _headerLabelStyle.alignment = TextAnchor.MiddleCenter;
                if (!EditorGUIUtility.isProSkin)
                    _headerLabelStyle.normal.textColor = new Color(0.4f, 0.4f, 0.4f, 1f);
                else
                    _headerLabelStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            }

            if (_statusStyle == null) {
                _statusStyle = new GUIStyle(EditorStyles.label);
                _statusStyle.fontStyle = FontStyle.Italic;
                _statusStyle.alignment = TextAnchor.MiddleRight;
            }

            if (_installedPackages == null) {
                EditorGUILayout.LabelField("Refreshing...", _headerLabelStyle, GUILayout.Height(30));
                return;
            }
            
            EditorGUILayout.LabelField("Infohazard Package Installer", _headerLabelStyle, GUILayout.Height(30));
            if (_canUseSubmodule) {
                EditorGUILayout.HelpBox("Git repository detected - you can use submodule mode.", MessageType.Info);
            } else {
                EditorGUILayout.HelpBox("Git not installed or project is not a git repo. You cannot use submodule mode.", MessageType.Warning);
            }

            using EditorGUI.DisabledScope scope = new EditorGUI.DisabledScope(_currentAction != null);
            
            _list.DoLayoutList();
            if (_list.selectedIndices.Count == 0) {
                EditorGUILayout.HelpBox("Select a package for more info.", MessageType.Info);
            } else {
                int index = _list.selectedIndices[0];
                ManagedPackageInfo package = ManagedPackageInfo.Packages[index];

                if (_selectedIndex != index || _selectedDependencies == null) {
                    _selectedIndex = index;
                    _selectedDependencies = new HashSet<string>();
                    ManagedPackageInfo.GetPackageDependencies(package.PackageIdentifier, _selectedDependencies);
                    _selectedDependencies.Remove(package.PackageIdentifier);
                }
                
                EditorGUILayout.LabelField(package.DisplayName, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(package.PackageIdentifier);
                EditorGUILayout.LabelField(package.Description, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Dependencies", EditorStyles.boldLabel);
                using (_ = new EditorGUI.IndentLevelScope(1)) {
                    foreach (string dependency in _selectedDependencies) {
                        EditorGUILayout.LabelField(dependency);
                    }
                }
            }
        }

        private void DrawElementCallback(Rect rect, int index, bool isActive, bool isFocused) {
            ManagedPackageInfo package = ManagedPackageInfo.Packages[index];
            EditorGUI.LabelField(rect, package.DisplayName);

            Rect buttonRect = rect;
            buttonRect.xMin = buttonRect.xMax - 150;
            int oldStatus = OptionNone;
            if (_installedPackages.TryGetValue(package.PackageIdentifier, out var packageInfo)) {
                if (packageInfo.Info.source == PackageSource.Git) {
                    oldStatus = OptionGitURL;
                } else if (packageInfo.Info.source == PackageSource.Embedded) {
                    oldStatus = packageInfo.IsSubmodule ? OptionSubmodule : OptionEmbedded;
                }
            }

            bool required = packageInfo?.DependedBy?.Count > 0;
            int newStatus;

            if (required) {
                newStatus = EditorGUI.Popup(buttonRect, oldStatus - 1, _installOptionsRequired) + 1;
            } else {
                newStatus = EditorGUI.Popup(buttonRect, oldStatus, _installOptions);
            }
            
            if (newStatus != oldStatus) {
                _currentAction = ChangePackageStatus(package, packageInfo?.Info, oldStatus, newStatus, true);
            }
        }

        private void DisplayError(Request request) {
            EditorUtility.DisplayDialog("PackageManager Request Failed", request.Error.ToString(), "OK");
        }

        private IEnumerator RefreshPackages() {
            ListRequest request = Client.List();
            while (!request.IsCompleted) {
                yield return null;
            }

            if (request.Status == StatusCode.Failure) {
                DisplayError(request);
            } else {
                _installedPackages = new Dictionary<string, InstalledManagedPackageInfo>();
                bool checkSubmodules = _canUseSubmodule && File.Exists(_gitModulesPath);
                foreach (PackageInfo info in request.Result) {
                    ManagedPackageInfo installable = ManagedPackageInfo.GetPackageInfo(info.name);
                    if (installable == null) continue;
                    InstalledManagedPackageInfo pkg = new InstalledManagedPackageInfo();
                    pkg.Info = info;
                    pkg.ManagedInfo = installable;
                    if (checkSubmodules && info.source == PackageSource.Embedded) {
                        foreach (string line in File.ReadLines(_gitModulesPath)) {
                            string trim = line.Trim();
                            if (trim == $"[submodule \"Packages/{installable.RepoName}\"]")
                            {
                                pkg.IsSubmodule = true;
                                break;
                            }
                        }
                    }
                    _installedPackages[info.name] = pkg;
                }

                foreach (var pair in _installedPackages) {
                    foreach (string dependency in pair.Value.ManagedInfo.Dependencies) {
                        if (!_installedPackages.TryGetValue(dependency, out InstalledManagedPackageInfo info)) {
                            continue;
                        }

                        if (info.DependedBy == null) info.DependedBy = new List<string>();
                        if (!info.DependedBy.Contains(pair.Key)) info.DependedBy.Add(pair.Key);
                    }
                }
            }
        }

        private static void DeleteDirectory(string targetDir) {
            string[] files = Directory.GetFiles(targetDir);
            string[] dirs = Directory.GetDirectories(targetDir);

            foreach (string file in files) {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }

            foreach (string dir in dirs) {
                DeleteDirectory(dir);
            }

            Directory.Delete(targetDir, false);
        }

        private IEnumerator ChangePackageStatus(ManagedPackageInfo package, PackageInfo installed, int oldStatus, int newStatus, bool refresh) {
            if (oldStatus == OptionGitURL) {
                RemoveRequest removeRequest = Client.Remove(package.PackageIdentifier);
                while (!removeRequest.IsCompleted) yield return null;
                if (removeRequest.Status == StatusCode.Failure) {
                    DisplayError(removeRequest);
                    yield break;
                }
            } else if (oldStatus == OptionEmbedded) {
                Directory.Delete(installed.resolvedPath, true);
            } else if (oldStatus == OptionSubmodule) {
                bool result = ExecuteProcess("git", $"rm -f Packages/{package.RepoName}", true);
                string dir = Path.Combine(Application.dataPath, "..", ".git", "modules", "Packages", package.RepoName);
                DeleteDirectory(dir);
                ExecuteProcess("git", $"config --remove-section submodule.Packages/{package.RepoName}", true);
                if (!result) yield break;
            }

            if (newStatus != OptionNone) {
                foreach (string dependency in package.Dependencies) {
                    if (_installedPackages.ContainsKey(dependency)) continue;
                    ManagedPackageInfo pkg = ManagedPackageInfo.GetPackageInfo(dependency);
                    IEnumerator changeStatus = ChangePackageStatus(pkg, null, OptionNone, newStatus, false);
                    while (changeStatus.MoveNext()) { }
                }
            }

            if (newStatus == OptionGitURL) {
                AddRequest addRequest = Client.Add(package.GitUrl);
                while (!addRequest.IsCompleted) yield return null;
                if (addRequest.Status == StatusCode.Failure) {
                    DisplayError(addRequest);
                }
            } else if (newStatus == OptionEmbedded) {
                using UnityWebRequest request = UnityWebRequest.Get(package.GitZipUrl);
                UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                while (!operation.isDone) yield return null;
                if (request.result != UnityWebRequest.Result.Success) {
                    EditorUtility.DisplayDialog("HTTP Request Failed", request.error, "OK");
                    yield break;
                }
                string tempPath = Path.GetTempFileName();
                string tempDir = Path.Combine(Path.GetTempPath(), $"Package_{package.PackageIdentifier}");
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                Directory.CreateDirectory(tempDir);
                
                File.WriteAllBytes(tempPath, request.downloadHandler.data);
                ZipFile.ExtractToDirectory(tempPath, tempDir);

                string unpackedDir = Directory.EnumerateDirectories(tempDir).First();
                
                Directory.Move(unpackedDir, Path.Combine(Application.dataPath, "..", "Packages", package.RepoName));
                DeleteDirectory(tempDir);
                File.Delete(tempPath);
            } else if (newStatus == OptionSubmodule) {
                bool result = ExecuteProcess("git", $"submodule add {package.GitUrl} Packages/{package.RepoName}",
                    true);
                if (!result) yield break;
            }

            if (refresh) {
                Client.Resolve();
                Refresh();
            }
        }
        
        private class InstalledManagedPackageInfo {
            public PackageInfo Info;
            public ManagedPackageInfo ManagedInfo;
            public bool IsSubmodule;
            public List<string> DependedBy;
        }
    }
}
