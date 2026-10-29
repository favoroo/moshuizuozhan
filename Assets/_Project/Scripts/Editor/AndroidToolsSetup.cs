using UnityEditor;
using UnityEngine;

namespace P1.Editor
{
    /// <summary>
    /// 一次性把 Android 外部工具路径写入 EditorPrefs（Preferences → External Tools）。
    /// </summary>
    [InitializeOnLoad]
    public static class AndroidToolsSetup
    {
        const string SdkRoot = "/Users/a1/Library/Android/sdk";
        const string NdkRoot = "/Applications/Unity/Hub/Editor/6000.6.7f1/PlaybackEngines/AndroidPlayer/NDK";
        const string JdkRoot = "/Library/Java/JavaVirtualMachines/temurin-17.jdk/Contents/Home";

        static AndroidToolsSetup()
        {
            EditorPrefs.SetString("AndroidSdkRoot", SdkRoot);
            EditorPrefs.SetString("AndroidNdkRoot", NdkRoot);
            EditorPrefs.SetString("JdkPath", JdkRoot);
        }
    }
}
