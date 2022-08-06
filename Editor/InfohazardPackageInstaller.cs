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
    public class InstallablePackage {
        public string DisplayName { get; set; }
        public string PackageIdentifier { get; set; }
        public string GitUrl { get; set; }
        public string GitZipUrl { get; set; }
        public string Description { get; set; }
        public string[] Dependencies { get; set; }

        public static readonly InstallablePackage[] Packages = {
            new InstallablePackage {
                DisplayName = "Core",
                PackageIdentifier = "com.infohazard.core",
                GitUrl = "https://github.com/vcmiller/Infohazard.Core.git",
                GitZipUrl = "https://api.github.com/repos/vcmiller/Infohazard.Core/zipball",
                Description = "A collection of useful functionality for Unity, which is used by my other packages.",
                Dependencies = Array.Empty<string>(),
            },
            new InstallablePackage {
                DisplayName = "Sequencing",
                PackageIdentifier = "com.infohazard.sequencing",
                GitUrl = "https://github.com/vcmiller/Infohazard.Sequencing.git",
                GitZipUrl = "https://api.github.com/repos/vcmiller/Infohazard.Sequencing/zipball",
                Description = "A system for creating startup and level loading sequences, as well as saving game state.",
                Dependencies = new[]{ "com.infohazard.core" }
            },
            new InstallablePackage {
                DisplayName = "State System",
                PackageIdentifier = "com.infohazard.statesystem",
                GitUrl = "https://github.com/vcmiller/Infohazard.StateSystem.git",
                GitZipUrl = "https://api.github.com/repos/vcmiller/Infohazard.StateSystem/zipball",
                Description = "A system for creating and applying states which override a component's values.",
                Dependencies = new[] { "com.infohazard.core" }
            },
            new InstallablePackage {
                DisplayName = "HyperNav",
                PackageIdentifier = "com.infohazard.hypernav",
                GitUrl = "https://github.com/vcmiller/Infohazard.HyperNav.git",
                GitZipUrl = "https://api.github.com/repos/vcmiller/Infohazard.HyperNav/zipball",
                Description = "A 3D volume-based navigation system.",
                Dependencies = new[]{ "com.infohazard.core" }
            },
            new InstallablePackage {
                DisplayName = "Slightly Better Rats",
                PackageIdentifier = "com.infohazard.slightlybetterrats",
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

        public static InstallablePackage GetPackageInfo(string identifier) =>
            Packages.FirstOrDefault(pkg => pkg.PackageIdentifier == identifier);

        public static void GetPackageDependencies(string identifier, HashSet<string> set) {
            InstallablePackage pkg = GetPackageInfo(identifier);
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
        private Dictionary<string, PackageInfo> _installedPackages;

        private IEnumerator _currentAction;

        [MenuItem("Infohazard/Installer")]
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

        private static bool CheckGitStatus() {
            ProcessStartInfo processInfo = new ProcessStartInfo("git", "status");
            Process process = Process.Start(processInfo);
            if (process != null) {
                process.WaitForExit();
                bool success = process.ExitCode == 0;
                process.Close();
                return success;
            } else {
                return false;
            }
        }

        private void OnEnable() {
            _list = new ReorderableList(InstallablePackage.Packages, typeof(InstallablePackage), false, false, false, false);
            _list.headerHeight = EditorGUIUtility.singleLineHeight;
            _list.drawElementCallback = DrawElementCallback;
            _list.multiSelect = false;
            _canUseSubmodule = CheckGitStatus();
            
            List<string> options = new List<string> {
                "Not Installed",
                "Git URL",
                "Embedded",
            };
            if (_canUseSubmodule) options.Add("Git Submodule");
            _installOptions = options.ToArray();
            _currentAction = RefreshPackages();

            EditorApplication.update += EditorApplication_Update;
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
                InstallablePackage package = InstallablePackage.Packages[index];

                if (_selectedIndex != index || _selectedDependencies == null) {
                    _selectedIndex = index;
                    _selectedDependencies = new HashSet<string>();
                    InstallablePackage.GetPackageDependencies(package.PackageIdentifier, _selectedDependencies);
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
            InstallablePackage package = InstallablePackage.Packages[index];
            EditorGUI.LabelField(rect, package.DisplayName);

            Rect buttonRect = rect;
            buttonRect.xMin = buttonRect.xMax - 150;
            int oldStatus = OptionNone;
            PackageInfo packageInfo;
            if (_installedPackages.TryGetValue($"{package.PackageIdentifier}@{package.GitUrl}", out packageInfo)) {
                oldStatus = OptionGitURL;
            } else if (_installedPackages.TryGetValue(package.PackageIdentifier, out packageInfo)) {
                
            }
            
            int newStatus = EditorGUI.Popup(buttonRect, oldStatus, _installOptions);
            if (newStatus == oldStatus) return;
            _currentAction = ChangePackageStatus(package, packageInfo, oldStatus, newStatus);
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
                _installedPackages = request.Result.ToDictionary(pkg => pkg.packageId);
            }
        }

        private IEnumerator ChangePackageStatus(InstallablePackage package, PackageInfo installed, int oldStatus, int newStatus) {
            if (oldStatus == OptionGitURL) {
                RemoveRequest removeRequest = Client.Remove(package.PackageIdentifier);
                while (!removeRequest.IsCompleted) yield return null;
                if (removeRequest.Status == StatusCode.Failure) {
                    DisplayError(removeRequest);
                    yield break;
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
                string tempPath = Path.GetTempFileName();
                File.WriteAllBytes(tempPath, request.downloadHandler.data);
                ZipFile.ExtractToDirectory(tempPath, "C:\\Users\\vmill\\Desktop");
            }
            
            Client.Resolve();
        }
    }
}
