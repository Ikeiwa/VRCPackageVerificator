/* Copyright (C) 2025 Ikeiwa - All Rights Reserved
 * You may use, distribute and modify this code under the
 * terms of the MIT license.
 */
using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEngine.Serialization;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
#endif

namespace Ikeiwa
{
    [Serializable]
    public class VersionError
    {
        [Tooltip("The error message")]
        public string message;
        [Tooltip("Text of the error button (leave empty for no button)")]
        public string buttonText;
        [Tooltip("Url the button sends to")]
        public string buttonURL;
        [Tooltip("The type of the message (will change message icon)")]
        public MessageType messageType = MessageType.Error;
    }
    
    [Serializable]
    public class PackageRequirement
    {
        [Tooltip("The id of the package (ex: com.vrchat.base)")]
        public string packageName;
        [Tooltip("The version of the package (ex: 3.7.0, leave empty for Any version)")]
        public string minVersion;
        [Tooltip("Message if the package is missing")]
        public VersionError missingError;
        [Tooltip("Message if the package version is wrong")]
        public VersionError versionError;
        [Tooltip("Script names equivalent for non VRC Creator Companion packages")]
        public string[] classesEquivalent = null;
    }
    
    [CreateAssetMenu(fileName = "PackageRules.asset", menuName = "Ikeiwa/Package Verificator/Package Rules")]
    public class PackageRules : ScriptableObject
    {
        [Tooltip("The name of your asset package")]
        public string packageName = "Package";
        [Tooltip("The thumbnail of your asset package (optional)")]
        public Texture2D packageThumbnail;
        [Tooltip("The version of your asset package (to force show the popup to project with old version installed)")]
        public string packageVersion = "1.0.0";
        [Tooltip("Minimum version of Unity the package requires")]
        public string requiredMinUnityVersion = "2022.3.22f1";
        [Tooltip("Asset/Folder to select when all packages are installed (optional)")]
        public Object assetToPing;
        [Tooltip("Messages to show if all packages are valid (optional)")]
        public List<VersionError> allValidMessages;
        [Tooltip("Packages required for this asset")]
        public List<PackageRequirement> packageRequirements;

        [Header("Advanced")]  
        [Tooltip("Text of the \"select package asset\" button in the verificator window")]
        public string pingAssetButtonText = "Select package Asset";
        [Tooltip("Text of the \"do not show again\" button in the verificator window")]
        public string doNotShowButtonText = "Do not show again";
        [Tooltip("Title of the verificator window (optional)")]
        public string popupTitle = string.Empty;

        private void Reset()
        {
            packageRequirements = new List<PackageRequirement>
            {
                new()
                {
                    packageName = "com.vrchat.base",
                    minVersion = "Any",
                    missingError = new VersionError{message = "Could not find VRChat SDK 3.0, please verify if it's installed.", buttonText = "Fix", buttonURL = "https://creators.vrchat.com/sdk/"},
                    versionError = new VersionError{message = "You are using an incompatible version of the SDK, please update it.", buttonText = "Fix", buttonURL = "https://creators.vrchat.com/sdk/updating-the-sdk"},
                }
            };
        }
    }
    
    #if UNITY_EDITOR
    [InitializeOnLoad]
    public static class PackageVerificator
    {
        static ListRequest Request;
        static bool hasChecked;
        public const string ignorePrefName = "PackageVerificatorDontShowAgain";

        static PackageVerificator()
        {
            Request = Client.List(true);
            EditorApplication.update += Progress;
        }

        static void Progress()
        {
            if (Request.IsCompleted && !hasChecked)
            {
                hasChecked = true;
                EditorApplication.update -= Progress;

                if (Request.Status >= StatusCode.Failure)
                    Debug.Log(Request.Error.message);

                CheckPackageRules();
            }
        }

        //[MenuItem("Tools/Package Verificator/Check Packages")]
        private static void ReShowPackageWindow()
        {
            CheckPackageRules(true);
        }

        private static void CheckPackageRules(bool ignoreDoNotShow = false)
        {
            var packageRules = AssetDatabase.FindAssets($"t: {nameof(PackageRules)}").ToList()
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadMainAssetAtPath).ToList();

            foreach (var packageRule in packageRules)
            {
                CheckInstall(packageRule as PackageRules, ignoreDoNotShow);
            }
        }

        public static void CheckInstall(PackageRules rules, bool ignoreDoNotShow = false)
        {
            if (!rules)
                return;
            
            if (!ignoreDoNotShow && EditorPrefs.HasKey(PlayerSettings.productName + "-" + ignorePrefName + "-" + rules.packageName + "-" + rules.packageVersion))
                return;
            
            List<VersionError> errors = new List<VersionError>();

#if !VRC_SDK_VRCSDK3
            errors.Add(new VersionError { message = "Could not find VRChat SDK 3.0, please verify if it's installed.", buttonText = "Fix", buttonURL = "https://creators.vrchat.com/sdk/" });
#else // !VRC_SDK_VRCSDK3
            if (!CompareVersions(SimplifyVersion(Application.unityVersion), SimplifyVersion(rules.requiredMinUnityVersion)))
            {
                errors.Add(new VersionError { message="You are using the wrong unity version, you need at least Unity 2022.3.6", buttonText = "Fix", buttonURL = "https://creators.vrchat.com/sdk/upgrade/unity-2022" });
            }
            else
            {
                if (Request != null && Request.Result != null)
                {
                    foreach (var requirement in rules.packageRequirements) 
                    {
                        var package = Request.Result.FirstOrDefault(p => p.name == requirement.packageName);

                        bool hasEquivalent = true;
                        if(requirement.classesEquivalent == null || requirement.classesEquivalent.Length == 0)
                            hasEquivalent = false;
                        else
                        {
                            foreach (var eqClass in requirement.classesEquivalent)
                            {
                                if(AssetDatabase.FindAssets("t:script " + eqClass).Length == 0)
                                {
                                    hasEquivalent = false;
                                    break;
                                }
                            }
                        }

                        if (!hasEquivalent)
                        {
                            if (package == null)
                                errors.Add(requirement.missingError);
                            else if (!CompareVersions(package.version, requirement.minVersion))
                                errors.Add(requirement.versionError);
                        }
                    }
                }
            }
#endif // !VRC_SDK_VRCSDK3

            if (errors.Count > 0)
                PackageVersionVerificatorWindow.DisplayError(rules,errors);
            else
                PackageVersionVerificatorWindow.DisplayValidPopup(rules);
        }

        private static bool CompareVersions(string installed, string required)
        {
            if(required == "Any" || string.IsNullOrEmpty(required))
                return true;

            System.Version installedVersion = null;
            System.Version requiredVersion = null;

            try
            {
                installedVersion = new System.Version(installed);
                requiredVersion = new System.Version(required);
            }
            catch(System.Exception ex)
            {
                return false;
            }
            

            return installedVersion >= requiredVersion;
        }

        private static string SimplifyVersion(string version)
        {
            if(string.IsNullOrEmpty(version))
                return version;
            
            for (int i = version.Length - 1; i >= 0; i--)
            {
                if (char.IsLetter(version[i]))
                {
                    return version.Substring(0,i);
                }
            }
            
            return version;
        }
    }

    public class PackageVersionVerificatorWindow : EditorWindow
    {
        private PackageRules rules;
        private List<VersionError> errors;
        private Vector2 scroll;
        private bool isValidPopup;

        public static void DisplayError(PackageRules rules, List<VersionError> errors)
        {
            if (errors == null || errors.Count == 0)
                return;

            PackageVersionVerificatorWindow window = CreateInstance<PackageVersionVerificatorWindow>();
            if(string.IsNullOrEmpty(rules.popupTitle))
                window.titleContent = new GUIContent(rules.packageName + " Install Verificator");
            else
                window.titleContent = new GUIContent(rules.popupTitle);
            window.rules = rules;
            window.errors = errors;
            window.minSize = new Vector2(500,window.GetPopupHeight(errors.Count));
            window.maxSize = window.minSize;
            window.ShowUtility();
        }

        public static void DisplayValidPopup(PackageRules rules)
        {
            if (!rules)
                return;
            
            List<VersionError> messages = new List<VersionError>();

            if (rules.allValidMessages == null || rules.allValidMessages.Count == 0)
            {
                messages = new List<VersionError>();
                messages.Add(new VersionError
                {
                    message = "All packages are installed correctly",
                    messageType = MessageType.Info
                });
            }
            else
            {
                messages = rules.allValidMessages;
            }

            PackageVersionVerificatorWindow window = CreateInstance<PackageVersionVerificatorWindow>();
            if(string.IsNullOrEmpty(rules.popupTitle))
                window.titleContent = new GUIContent(rules.packageName + " Install Verificator");
            else
                window.titleContent = new GUIContent(rules.popupTitle);
            window.isValidPopup = true;
            window.rules = rules;
            window.errors = messages;
            window.minSize = new Vector2(500,window.GetPopupHeight(messages.Count));
            window.maxSize = window.minSize;
            window.ShowUtility();
        }

        void OnGUI()
        {
            if (errors == null || errors.Count == 0)
            {
                Close();
                return;
            }

            EditorGUILayout.BeginScrollView(scroll);

            DrawHeader();

            foreach (var error in errors)
            {
                DrawIssueBox(error);
            }

            if (rules.assetToPing && isValidPopup)
            {
                if(GUILayout.Button(rules.pingAssetButtonText))
                {
                    Selection.activeObject = rules.assetToPing;
                    EditorGUIUtility.PingObject(Selection.activeObject);
                }
            }
            
            EditorGUILayout.Space();

            if(GUILayout.Button(rules.doNotShowButtonText))
            {
                EditorGUILayout.EndScrollView();
                EditorPrefs.SetBool(PlayerSettings.productName + "-" + PackageVerificator.ignorePrefName + "-" + rules.packageName + "-" + rules.packageVersion, true);
                Close();
                return;
            }
            
            if (GUILayout.Button("Package Verificator by Ikeiwa", GetLinkStyle()))
            {
                Application.OpenURL("https://ikeiwa.dev/");
            }
            Rect buttonRect = GUILayoutUtility.GetLastRect();
            EditorGUIUtility.AddCursorRect(buttonRect, MouseCursor.Link);

            EditorGUILayout.EndScrollView();
        }

        void DrawHeader()
        {
            if (!rules.packageThumbnail)
                return;
            
            float imageRatio = (float)rules.packageThumbnail.height / (float)rules.packageThumbnail.width;
            int imgHeight = (int)(500 * imageRatio);
            
            GUI.DrawTexture(position, rules.packageThumbnail, ScaleMode.ScaleToFit);
            GUI.DrawTexture(new Rect(0,0,500, imgHeight), rules.packageThumbnail, ScaleMode.ScaleToFit, true);
            EditorGUILayout.Space(imgHeight);
        }

        void DrawIssueBox(VersionError error)
        {
            if (error == null)
                return;

            bool haveButtons = !string.IsNullOrEmpty(error.buttonText);

            GUIStyle style = new GUIStyle("HelpBox");
            float width = haveButtons ? position.width - 110 : position.width - 20;
            if (haveButtons)
                style.fixedWidth = width;

            float minHeight = 40;

            EditorGUILayout.BeginHorizontal();

            GUIContent c = new GUIContent(error.message);
            float height = style.CalcHeight(c, width - 32);
            Rect rt = GUILayoutUtility.GetRect(c, style, GUILayout.MinHeight(Mathf.Max(minHeight, height)));
            EditorGUI.HelpBox(rt, error.message, error.messageType);

            if (haveButtons)
            {
                EditorGUILayout.BeginVertical();
                float buttonHeight = minHeight;

                if (GUILayout.Button(error.buttonText, GUILayout.Height(buttonHeight)))
                {
                    Application.OpenURL(error.buttonURL);
                    Repaint();
                }
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndHorizontal();
        }

        private int GetPopupHeight(int messageCount)
        {
            int height = 40 * messageCount + 50;
            if (isValidPopup && rules.assetToPing)
                height += 30;

            if (rules.packageThumbnail)
            {
                float imageRatio = (float)rules.packageThumbnail.height / (float)rules.packageThumbnail.width;
                
                height += (int)(500 * imageRatio);
            }

            return height;
        }
        
        GUIStyle GetLinkStyle()
        {
            var style = new GUIStyle();
            style.alignment = TextAnchor.UpperRight;
            style.normal.textColor = new Color(0.28f, 0.57f, 0.81f);
            style.fontStyle = FontStyle.Italic;
            var border = style.border;
            border.left = 0;
            border.top = 0;
            border.right = 0;
            border.bottom = 0;
            return style;
        }
    }
    
    
    public class PackageVersionVerificatorListWindow : EditorWindow
    {
        private Vector2 scroll;
        private List<Object> rules;

        [MenuItem("Tools/Package Verificator/Check Packages")]
        public static void Show()
        {
            var packageRules = AssetDatabase.FindAssets($"t: {nameof(PackageRules)}").ToList()
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadMainAssetAtPath).ToList();
            
            PackageVersionVerificatorListWindow window = CreateInstance<PackageVersionVerificatorListWindow>();
            window.titleContent = new GUIContent("Select a package");
            window.minSize = new Vector2(250,400);
            window.maxSize = window.minSize;
            window.rules = packageRules;
            window.ShowUtility();
        }

        void OnGUI()
        {
            EditorGUILayout.BeginScrollView(scroll);

            if (rules == null || rules.Count == 0)
            {
                EditorGUILayout.LabelField("No packages with verificators are installed");
            }
            else
            {
                foreach (var ruleObj in rules)
                {
                    PackageRules rule = (PackageRules)ruleObj;
                    
                    if(!rule)
                        continue;

                    if (GUILayout.Button(rule.packageName))
                    {
                        PackageVerificator.CheckInstall(rule);
                    }
                }
            }
            
            EditorGUILayout.EndScrollView();
        }
    }
#endif
}

