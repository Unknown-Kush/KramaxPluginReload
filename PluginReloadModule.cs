//
// This file is part of the KSPPluginReload plugin for Kerbal Space Program, Copyright Joop Selen
// License: http://creativecommons.org/licenses/by-nc-sa/3.0/
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Policy;
using System.Text;
using KSP.IO;
using KramaxPluginReload.Classess;
using KramaxPluginReload.UI;
using UnityEngine;
using System.Reflection.Emit;
using System.Threading;
using System.Diagnostics;
using System.IO;

[KSPAddon(KSPAddon.Startup.Instantly, true)]
public class KramaxPluginReloadModule : MonoBehaviour
{
    public KramaxPluginReloadModule()
    {
        KramaxPluginReload.Classe.Immortal.AddImmortal<KramaxPluginReload.PluginReloadModule>();
    }

}

namespace KramaxPluginReload
{
    static public class Deb
    {
        public static void Log(String format, params System.Object[] args)
        {
            UnityEngine.Debug.Log(String.Format(format, args));
        }

        public static void Log(String message)
        {
            UnityEngine.Debug.Log(message);
        }
    }


    /*
    public class Reloadable : Attribute
    {
        public string Comment { get; set; }
    }
    */

    public class PluginReloadModule : MonoBehaviour
    {
        public bool GUIActive;
        static public int versionCount = 0;
        public static List<PluginClass> PluginClasses = new List<PluginClass>();
        public static List<PluginSetting> PluginSettings = new List<PluginSetting>();
        public static String windowsSdkBinPath = null;
        public static String dotFrameworkBinPath = null;
        public static PluginReloadWindow PluginReloadWindow = new PluginReloadWindow()
        {
            ReloadCallback = LoadPlugins
        };

        public PluginReloadModule()
        {
            Deb.Log("KramaxPluginReload loaded, Version: {0}.",
                Assembly.GetExecutingAssembly().GetName().Version);

            LoadConfig();
            LoadPlugins();
            PluginReloadWindow.OpenWindow();
        }

        private static void LoadConfig()
        {
            try
            {
                ConfigNode settings = ConfigNode.Load(KSPUtil.ApplicationRootPath + "GameData/KramaxPluginReload/Settings.cfg");

                windowsSdkBinPath = settings.GetValue("windowsSdkBinPath");
                dotFrameworkBinPath = settings.GetValue("dotFrameworkBinPath");
                foreach (ConfigNode node in settings.GetNodes("PluginSetting"))
                {
                    PluginSetting pluginSetting = new PluginSetting()
                    {
                        Name = node.GetValue("name"),
                        Path = node.GetValue("path"),
                        LoadOnce = bool.Parse(node.GetValue("loadOnce")),
                        MethodsAllowedToFail = bool.Parse(node.GetValue("methodsAllowedToFail"))
                    };

                    PluginSettings.Add(pluginSetting);
                }
            }
            catch (Exception ex)
            {
                Deb.Log("KramaxPluginReload: Failed to load settings.cfg. Error:\n{0}", ex);
                return;
            }
        }

        private static Assembly LoadAssembly(string location)
        {
            try
            {
                //Therere is a bug in Mono that prevents loading assembly with the same name more than once
                //(if such assembly is loaded then old version is returned). The bug report can be 
                //found here: https://xamarin.github.io/bugzilla-archives/11/11199/bug.html.
                //The bug is corrected in mono-6.6.0.161 and mono-5.16.0.179 
                //(commit https://github.com/mono/mono/commit/40c13f7b0ff71bfff8e58f8bd66bca0734d7d284 ) 
                //but mono used in KSP 1.8.1 is reported as "5.11.0 (Visual Studio built mono)". 
                //To hack around this we do the following:
                // - Decompile .dll using ildasm
                // - Change assembly name inside decomiled file (fragment ".assembly <assembly-name>")
                // - Compile file again to .dll using ilasm
                // - Load changed .dll
                //example location of ildasm is C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools\
                //example location of ilasm is C:\Windows\Microsoft.NET\Framework\v4.0.30319\.
                //
                //I do not know why this was working correctly in previous versions of KSP. Either the bug manifests
                //itself only on Windows (previously I was developing on OsX) or there was some change in Mono in Unity3d.
                //
                //
                if (!System.IO.File.Exists(location))
                {
                    Deb.Log("File does not exist: {0}", location);
                    return null;
                }
                List<string> filesToRemoveAtEnd = new List<string>();
                String locationToRead = location;
                if (windowsSdkBinPath != null && dotFrameworkBinPath != null)
                {
                    Deb.Log("Paths to ildasm and ilasm are provided. Will change the assembly name");
                    String oldName = Path.GetFileNameWithoutExtension(location);
                    String newName = oldName + "v" + versionCount;
                    String directoryForIntermediateOutput = Path.GetDirectoryName(location);
                    String decompiledPath = Path.Combine(directoryForIntermediateOutput, newName + ".decompiled");
                    filesToRemoveAtEnd.Add(decompiledPath);
                    filesToRemoveAtEnd.Add(Path.Combine(directoryForIntermediateOutput, newName + ".res"));

                    Deb.Log("Running ildasm");
                    RunProcess(windowsSdkBinPath, "ildasm", location, "/output=" + decompiledPath, "/nobar");

                    Deb.Log("Substituting assembly name");
                    string[] lines = System.IO.File.ReadAllLines(decompiledPath);
                    for (int i = 0; i < lines.Length; ++i)
                    {
                        String line = lines[i];
                        if (line.Contains(".assembly " + oldName))
                        {
                            line = line.Replace(".assembly " + oldName, ".assembly " + newName);
                        }
                        lines[i] = line;
                    }
                    System.IO.File.WriteAllLines(decompiledPath, lines);

                    Deb.Log("Running ildasm");
                    RunProcess(dotFrameworkBinPath, "ilasm", decompiledPath, "/dll");
                    locationToRead = Path.Combine(directoryForIntermediateOutput, newName + ".dll");
                    filesToRemoveAtEnd.Add(locationToRead);
                }
                byte[] assemblyBytes = System.IO.File.ReadAllBytes(locationToRead);
                Assembly a = Assembly.Load(assemblyBytes);
                Deb.Log("Reloaded assembly: {0} version: {1}.", a.GetName().Name, a.GetName().Version);
                foreach (String fileToRemove in filesToRemoveAtEnd)
                {
                    System.IO.File.Delete(fileToRemove);
                }
                return a;
            }
            catch (Exception ex)
            {
                Deb.Log("KramaxPluginReload: Failed to load plugin from file {0}. Error:\n\n{1}", location, ex);
            }
            return null;
        }
        static void RunProcess(String execPath, String execName, params String[] arguments)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            Process p = new Process();

            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardInput = true;

            startInfo.UseShellExecute = false;
            startInfo.Arguments = string.Join(" ", arguments.Select(e => "\"" + e + "\"")); ;
            startInfo.FileName = Path.Combine(execPath, execName);

            p.StartInfo = startInfo;
            p.Start();
            p.StandardOutput.ReadToEnd();
            p.WaitForExit();
        }

        static Type CreateUniqueSubClass(ModuleBuilder moduleBldr, Type originalType, int versionUid)
        {

            String newClassName = String.Format("{0}_{1}_", originalType.Name, versionUid);

            Deb.Log("CreateUniqueSubClass: new class name is {0}", newClassName);


            TypeBuilder typeBldr =
                moduleBldr.DefineType(newClassName, TypeAttributes.Public | TypeAttributes.Class, originalType);

            ConstructorBuilder ctor = typeBldr.DefineDefaultConstructor(MethodAttributes.Public);

            return typeBldr.CreateType();
        }

        private static void LoadPlugins()
        {
            versionCount = versionCount + 1;

            Deb.Log("KramaxPluginReload: (Re)loading plugins with version {0}.", versionCount);

            Type type = typeof(KramaxReloadExtensions.ReloadableMonoBehaviour);

            foreach (PluginSetting setting in PluginSettings)
            {
                //Skip reloading of loadonce assemblies
                if (setting.LoadOnce == true) continue;

                //Call ondestroy on alive classes
                List<PluginClass> toRemove = new List<PluginClass>();

                foreach (PluginClass pluginClass in PluginClasses.Where(pc => pc.pluginSetting == setting))
                {
                    if (pluginClass.alive)
                    {
                        pluginClass.DeleteInstance();
                    }
                    toRemove.Add(pluginClass);
                }

                //Remove old class references
                foreach (PluginClass r in toRemove)
                {
                    PluginClasses.Remove(r);
                }

                var assembly = LoadAssembly(setting.Path);

                //Remove assembly if reloading failed
                if (assembly == null)
                {
                    foreach (PluginClass pluginClass in PluginClasses.Where(pc => pc.pluginSetting == setting).ToList())
                    {
                        PluginClasses.Remove(pluginClass);
                    }
                    continue;
                }

                String tmpAssemblyName = String.Format("KramaxPIRLAsmb_{0}", versionCount);
                String tmpModuleName = String.Format("KramaxPIRLMod_{0}", versionCount);

                AssemblyBuilder assemblyBldr =
                    Thread.GetDomain().DefineDynamicAssembly(new AssemblyName(tmpAssemblyName),
                                                             AssemblyBuilderAccess.Run);

                ModuleBuilder moduleBldr = assemblyBldr.DefineDynamicModule(tmpModuleName);

                IList<Type> derivedClassess =
                  (from t in assembly.GetTypes()
                   where t.IsSubclassOf(typeof(MonoBehaviour))
                   select t).ToList();

                List<PluginClass> plugins = new List<PluginClass>();
                Dictionary<Type, Type> typeMapping = new Dictionary<Type, Type>();

                foreach (var derivedClass in derivedClassess)
                {
                    Deb.Log("KramaxPluginReload.LoadPlugins: got type {0}.", derivedClass.Name);
                    Deb.Log("KramaxPluginReload.LoadPlugins: from assembly {0}, v{1}.",
                        derivedClass.Assembly.GetName().Name,
                        derivedClass.Assembly.GetName().Version);

                    System.Attribute[] attrs = System.Attribute.GetCustomAttributes(derivedClass);

                    KSPAddon kspAddon = null;

                    foreach (var att in attrs)
                    {
                        if (att is KSPAddon)
                        {
                            kspAddon = att as KSPAddon;
                        }
                    }

                    if (!derivedClass.IsSubclassOf(typeof(KramaxReloadExtensions.ReloadableMonoBehaviour)))
                    {
                        Deb.Log("KramaxPluginReload.LoadPlugins: ERROR type {0} is not ReloadableMonoBehaviour subclass.",
                            derivedClass.Name);
                        continue;
                    }

                    if (kspAddon != null)
                    {
                        Deb.Log("KramaxPluginReload.LoadPlugins: type {0} will be top-level component.", derivedClass.Name);

                        PluginClass pluginClass = new PluginClass();

                        pluginClass.pluginSetting = setting;
                        pluginClass.originalType = derivedClass;
                        pluginClass.kspAddon = kspAddon;
                        pluginClass.type = CreateUniqueSubClass(moduleBldr, derivedClass, versionCount);

                        plugins.Add(pluginClass);
                    }
                    else
                    {
                        Deb.Log("KramaxPluginReload.LoadPlugins: type {0} will be sub-level component.", derivedClass.Name);

                        var newType = CreateUniqueSubClass(moduleBldr, derivedClass, versionCount);
                        typeMapping[derivedClass] = newType;
                    }
                }

                foreach (var plugin in plugins)
                {
                    plugin.typeMapping = typeMapping;
                    PluginClasses.Add(plugin);
                }
            }

            foreach (var pluginClass in PluginClasses)
            {
                if (pluginClass.kspAddon.once == false || pluginClass.fired == false)
                {
                    bool awake = false;
                    switch (pluginClass.kspAddon.startup)
                    {
                        case KSPAddon.Startup.Instantly:
                        case KSPAddon.Startup.EveryScene:
                        //TODO: Check wether PSystem should even respawn.
                        case KSPAddon.Startup.PSystemSpawn:
                            awake = true;
                            break;
                        case KSPAddon.Startup.Credits:
                            awake = (HighLogic.LoadedScene == GameScenes.CREDITS);
                            break;
                        case KSPAddon.Startup.EditorAny:
                            awake = (HighLogic.LoadedScene == GameScenes.EDITOR);
                            break;
                        case KSPAddon.Startup.Flight:
                            awake = (HighLogic.LoadedScene == GameScenes.FLIGHT);
                            break;
                        case KSPAddon.Startup.MainMenu:
                            awake = (HighLogic.LoadedScene == GameScenes.MAINMENU);
                            break;
                        case KSPAddon.Startup.Settings:
                            awake = (HighLogic.LoadedScene == GameScenes.SETTINGS);
                            break;
                        case KSPAddon.Startup.SpaceCentre:
                            awake = (HighLogic.LoadedScene == GameScenes.SPACECENTER);
                            break;
                        case KSPAddon.Startup.TrackingStation:
                            awake = (HighLogic.LoadedScene == GameScenes.TRACKSTATION);
                            break;
                    }

                    if (awake)
                    {
                        Deb.Log("KramaxPluginReload.LoadPlugins: plugin should be awake: {0}.", pluginClass.Name);
                        pluginClass.CreateInstance();
                    }
                }
            }
            Deb.Log("KramaxPluginReload.LoadPlugins: Plugins (re)loaded.");
        }

        public void Update()
        {
            if ((Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) && Input.GetKeyDown(KeyCode.P))
            {
                if (PluginReloadWindow.Visible == false)
                    PluginReloadWindow.OpenWindow();
                else
                    PluginReloadWindow.CloseWindow();

            }
        }
    }
}
