#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

/// <summary>
/// Every iOS build: adds the sentence Apple's tracking popup shows
/// (NSUserTrackingUsageDescription) to Info.plist, links the
/// AppTrackingTransparency framework, and adds the advertisers'
/// SKAdNetwork IDs Unity Ads asks for. Without the sentence, iOS will not
/// show the popup and App Review rejects the build.
/// </summary>
public static class IosTrackingPostProcess
{
    const string Reason =
        "This lets us show you more relevant ads and keeps Driftline Eternal free to play.";

    // From the Unity Ads dashboard's "missing SKAdNetwork IDs" list. Unity
    // says to check that list monthly: paste any new ones in here.
    static readonly string[] SkAdNetworkIds =
    {
        "tl55sbb4fm.skadnetwork",
        "m8dbw4sv7c.skadnetwork",
        "klf5c3l5u5.skadnetwork",
        "a8cz6cu7e5.skadnetwork",
        "mj797d8u6f.skadnetwork",
        "9t245vhmpl.skadnetwork",
        "yclnxrl5pm.skadnetwork",
        "uw77j35x4d.skadnetwork",
        "prcb7njmu6.skadnetwork",
        "4pfyvq9l8r.skadnetwork",
        "pwa73g5rt2.skadnetwork",
        "mlmmfzh3r3.skadnetwork",
        "v9wttpbfk9.skadnetwork",
        "v79kvwwj4g.skadnetwork",
        "4fzdc2evr5.skadnetwork",
        "wzmmz9fp6w.skadnetwork",
        "97r2b46745.skadnetwork",
        "p78axxw29g.skadnetwork",
        "5a6flpkh64.skadnetwork",
        "lr83yxwka7.skadnetwork",
        "4w7y6s5ca2.skadnetwork",
        "c6k4g5qg8m.skadnetwork",
        "6yxyv74ff7.skadnetwork",
        "s39g8k73mm.skadnetwork",
        "2fnua5tdw4.skadnetwork",
        "5l3tpt7t6e.skadnetwork",
        "w9q455wk68.skadnetwork",
        "ydx93a7ass.skadnetwork",
        "mp6xlyr22a.skadnetwork",
        "vhf287vqwu.skadnetwork",
        "4468km3ulz.skadnetwork",
        "n9x2a789qt.skadnetwork",
        "kbd757ywx3.skadnetwork",
        "238da6jt44.skadnetwork",
        "7ug5zh24hu.skadnetwork",
        "44jx6755aq.skadnetwork",
        "294l99pt4k.skadnetwork",
        "e5fvkxwrpn.skadnetwork",
        "f73kdq92p3.skadnetwork",
        "4dzt52r2t5.skadnetwork",
        "f7s53z58qe.skadnetwork",
        "av6w8kgt66.skadnetwork",
        "5f5u5tfb26.skadnetwork",
        "77y3x8wds4.skadnetwork",
        "9rd848q2bz.skadnetwork",
        "x44k69ngh6.skadnetwork",
        "wg4vff78zm.skadnetwork",
        "9nlqeag3gk.skadnetwork",
        "22mmun2rn5.skadnetwork",
        "3sh42y64q3.skadnetwork",
        "ppxm28t8ap.skadnetwork",
        "cstr6suwn9.skadnetwork",
        "3rd42ekr43.skadnetwork",
        "3qy4746246.skadnetwork",
        "g6gcrrvk4p.skadnetwork",
        "zq492l623r.skadnetwork",
        "32z4fx6l9h.skadnetwork",
        "a2p9lx4jpn.skadnetwork",
        "5lm9lj6jb7.skadnetwork",
        "feyaarzu9v.skadnetwork",
        "xga6mpmplv.skadnetwork",
        "8s468mfl3y.skadnetwork",
        "578prtvx9j.skadnetwork",
        "hs6bdukanm.skadnetwork",
        "k6y4y55b64.skadnetwork",
        "424m5254lk.skadnetwork",
        "5tjdwbrq8w.skadnetwork",
        "v72qych5uu.skadnetwork",
        "k674qkevps.skadnetwork",
        "t38b2kh725.skadnetwork",
        "2u9pt9hc89.skadnetwork",
        "glqzh8vgby.skadnetwork",
        "mqn7fxpca7.skadnetwork",
        "488r3q3dtq.skadnetwork",
        "f38h382jlk.skadnetwork",
        "zmvfpc5aq8.skadnetwork",
    };

    [PostProcessBuild(100)]
    public static void OnPostProcessBuild(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS) return;

        string plistPath = Path.Combine(path, "Info.plist");
        var plist = new PlistDocument();
        plist.ReadFromFile(plistPath);
        plist.root.SetString("NSUserTrackingUsageDescription", Reason);

        // the advertisers' SKAdNetwork IDs, added to whatever LevelPlay
        // already put there, without doubling any up
        PlistElementArray items = plist.root.values.ContainsKey("SKAdNetworkItems")
            ? plist.root["SKAdNetworkItems"].AsArray()
            : plist.root.CreateArray("SKAdNetworkItems");
        var have = new System.Collections.Generic.HashSet<string>();
        foreach (var item in items.values)
        {
            var dict = item.AsDict();
            if (dict != null && dict.values.ContainsKey("SKAdNetworkIdentifier"))
                have.Add(dict["SKAdNetworkIdentifier"].AsString().ToLowerInvariant());
        }
        foreach (string id in SkAdNetworkIds)
        {
            if (!have.Add(id.ToLowerInvariant())) continue;
            items.AddDict().SetString("SKAdNetworkIdentifier", id);
        }
        plist.WriteToFile(plistPath);

        string projPath = PBXProject.GetPBXProjectPath(path);
        var proj = new PBXProject();
        proj.ReadFromFile(projPath);
        proj.AddFrameworkToProject(proj.GetUnityFrameworkTargetGuid(), "AppTrackingTransparency.framework", true);
        proj.WriteToFile(projPath);
    }

    /// <summary>
    /// Unity's own UnityRuntime.framework says in its Info.plist that it runs
    /// on iOS 15, while its code is built for a newer iOS (17, with Unity 6.6).
    /// App Store Connect rejects any framework whose Info.plist and code
    /// disagree - ITMS-90208, "does not support the minimum OS Version
    /// specified in the Info.plist" - which is what happened to build 2 of
    /// 1.0.3. This sets the framework's MinimumOSVersion to the iOS its code
    /// was built for, and makes sure the app itself asks for no less.
    /// </summary>
    [PostProcessBuild(200)]
    public static void FixUnityRuntimeMinimumOS(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS) return;

        string projPath = PBXProject.GetPBXProjectPath(path);
        var proj = new PBXProject();
        proj.ReadFromFile(projPath);
        string appMinimum = proj.GetBuildPropertyForAnyConfig(proj.GetUnityMainTargetGuid(),
                                                              "IPHONEOS_DEPLOYMENT_TARGET");
        if (string.IsNullOrEmpty(appMinimum)) appMinimum = PlayerSettings.iOS.targetOSVersionString;

        string want = appMinimum;
        var frameworkPlists = new System.Collections.Generic.List<string>();
        foreach (string plistPath in Directory.GetFiles(path, "Info.plist", SearchOption.AllDirectories))
        {
            string folder = Path.GetFileName(Path.GetDirectoryName(plistPath));
            if (folder != "UnityRuntime.framework") continue;
            frameworkPlists.Add(plistPath);
            // what the code inside was actually built for, when it can be read
            string builtFor = BinaryMinimumOS(Path.Combine(Path.GetDirectoryName(plistPath), "UnityRuntime"));
            want = HigherVersion(want, builtFor);
        }
        if (frameworkPlists.Count == 0 || string.IsNullOrEmpty(want)) return;

        foreach (string plistPath in frameworkPlists)
        {
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetString("MinimumOSVersion", want);
            plist.WriteToFile(plistPath);
        }

        // the app may not promise an older iOS than the framework it carries
        if (want != appMinimum)
        {
            foreach (string guid in new[] { proj.ProjectGuid(), proj.GetUnityMainTargetGuid(),
                                             proj.GetUnityFrameworkTargetGuid() })
                proj.SetBuildProperty(guid, "IPHONEOS_DEPLOYMENT_TARGET", want);
            proj.WriteToFile(projPath);
        }
        UnityEngine.Debug.Log("UnityRuntime.framework minimum iOS set to " + want +
                              " (app minimum was " + appMinimum + ")");
    }

    /// <summary>
    /// The minimum iOS a compiled binary was built for (its LC_BUILD_VERSION
    /// "minos"), read with Xcode's vtool. Null if it cannot be read.
    /// </summary>
    static string BinaryMinimumOS(string binaryPath)
    {
        if (!File.Exists(binaryPath)) return null;
        try
        {
            var info = new System.Diagnostics.ProcessStartInfo("xcrun",
                "vtool -show-build \"" + binaryPath + "\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using (var process = System.Diagnostics.Process.Start(info))
            {
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(10000);
                foreach (string line in output.Split('\n'))
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("minos "))
                        return trimmed.Substring(6).Trim();
                }
            }
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogWarning("Could not read the minimum iOS of " + binaryPath + ": " + e.Message);
        }
        return null;
    }

    /// <summary>The higher of two iOS versions such as "15.0" and "17.0".</summary>
    static string HigherVersion(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b;
        if (string.IsNullOrEmpty(b)) return a;
        System.Version va, vb;
        if (!System.Version.TryParse(a.Contains(".") ? a : a + ".0", out va)) return b;
        if (!System.Version.TryParse(b.Contains(".") ? b : b + ".0", out vb)) return a;
        return vb > va ? b : a;
    }
}
#endif
