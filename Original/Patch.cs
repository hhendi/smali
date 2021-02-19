using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Ionic.Zip;
using Ionic.Zlib;

namespace SmaliPatcher
{
    public class Patch
    {
        private Adb _adb;
        private bool _dexPatcherCoreRequired;
        private string _dexPatcherTarget;
        private Download _download;
        private bool _hasBeenDeodexed;
        private MainForm _mainForm;
        private readonly List<string> _processedFiles = new List<string>();
        public List<Patches> Patches;

        public void Init(object sender)
        {
            _mainForm ??= (MainForm) sender;
        }

        public void ProcessFrameworkDirectory(string folderPath)
        {
            if (_download == null)
            {
                _download = new Download();
                _download.Init(_mainForm);
            }
            if (Directory.Exists("apk")) Directory.Delete("apk", true);
            string apiLevel = "00";
            if (File.Exists(folderPath + "\\build.prop"))
            {
                string[] array = File.ReadAllLines(folderPath + "\\build.prop");
                if (array.Length != 0)
                    for (int i = 0; i < array.Length; i++)
                        if (array[i].Contains("ro.build.version.sdk="))
                        {
                            apiLevel = array[i].Substring(21, 2);
                            break;
                        }
            }
            _mainForm.DebugUpdate("\n==> Processing framework");
            _processedFiles.Clear();
            foreach (Patches patch in _mainForm.Patches)
                if (GetPatchStatus(patch.PatchTitle) && !_processedFiles.Contains(patch.TargetFile))
                {
                    _processedFiles.Add(patch.TargetFile);
                    string text2 = "";
                    string targetFile = patch.TargetFile;
                    string text3 = targetFile.Split('.')[0];
                    string[] files = Directory.GetFiles(folderPath, targetFile, SearchOption.AllDirectories);
                    if (files.Length == 1)
                        text2 = Path.GetDirectoryName(files[0]);
                    else if (files.Length > 1)
                    {
                        _mainForm.DebugUpdate("\n!!! ERROR: Multiple " + targetFile + " found.");
                        _mainForm.StatusUpdate("ERROR..");
                        return;
                    }
                    if (Directory.Exists(text2))
                    {
                        bool flag = false;
                        using (ZipFile val = ZipFile.Read(files[0]))
                            if (val.ContainsEntry("classes.dex")) flag = true;
                        if (apiLevel != "00") _mainForm.DebugUpdate("\n==> Detected API: " + apiLevel);
                        string[] files2 = Directory.GetFiles(text2, text3 + ".odex", SearchOption.AllDirectories);
                        if (files2.Length == 0)
                            files2 = Directory.GetFiles(text2, "boot-" + text3 + ".odex", SearchOption.AllDirectories);
                        string[] files3 = Directory.GetFiles(text2, text3 + ".vdex", SearchOption.AllDirectories);
                        if (files3.Length == 0)
                            files3 = Directory.GetFiles(text2, "boot-" + text3 + ".vdex", SearchOption.AllDirectories);
                        string[] files4 = Directory.GetFiles(text2, text3 + ".oat", SearchOption.AllDirectories);
                        if (files4.Length == 0)
                            files4 = Directory.GetFiles(text2, "boot-" + text3 + ".oat", SearchOption.AllDirectories);
                        string text4 = "";
                        if (files2.Length != 0 && files2[0].Contains("arm"))
                        {
                            text4 = new Regex("\\b\\\\arm.*\\\\\\b").Match(files2[0]).Value;
                            text4 = text4.Substring(0, text4.Length - 1);
                        }
                        if (text4 == "" && files4.Length != 0)
                            foreach (string text5 in files4)
                                if (text5.Contains("arm"))
                                {
                                    text4 = new Regex("\\b\\\\arm.*\\\\\\b").Match(text5).Value;
                                    text4 = text4.Substring(0, text4.Length - 1);
                                }
                        if (File.Exists(text2 + "\\" + targetFile))
                        {
                            long num = 0L;
                            long num2 = 0L;
                            long num3 = 0L;
                            string[] array2 = files2;
                            foreach (string fileName in array2) num += new FileInfo(fileName).Length;
                            array2 = files3;
                            foreach (string fileName2 in array2) num2 += new FileInfo(fileName2).Length;
                            array2 = files4;
                            foreach (string fileName3 in array2) num3 += new FileInfo(fileName3).Length;
                            if (flag)
                                JarDecompile(text2 + "\\" + targetFile);
                            else if (files2.Length != 0 && files3.Length == 0 && num > 0)
                                OdexDeodex(files2[0], targetFile, text2 + text4, apiLevel);
                            else if (files3.Length != 0 && num2 > 0)
                                VdexExtract(files3[0], files[0]);
                            else if (!flag && files4.Length != 0 && num3 > 0)
                                OdexDeodex(text2 + text4 + "\\" + Path.GetFileName(files4[0]), targetFile,
                                    text2 + text4, apiLevel);
                            else if (!flag && files2.Length == 0 && files3.Length == 0 && files4.Length == 0)
                            {
                                _mainForm.DebugUpdate("\n!!! ERROR: Incomplete framework dump, required files missing.");
                                _mainForm.DebugUpdate(
                                    "\n\nYou can try running the patcher while booted into recovery mode with /system mounted, it may fix this.");
                                _mainForm.StatusUpdate("ERROR..");
                                return;
                            }
                        }
                        else if (!File.Exists(text2 + "\\" + targetFile))
                        {
                            _mainForm.DebugUpdate("\n!!! ERROR: " + targetFile + " not found.");
                            _mainForm.StatusUpdate("ERROR..");
                            return;
                        }
                    }
                    else if (!Directory.Exists(text2))
                    {
                        _mainForm.DebugUpdate("\n!!! ERROR: Base directory not found.");
                        _mainForm.StatusUpdate("ERROR..");
                        return;
                    }
                }
            if (Directory.Exists("apk") && Directory.GetFiles("apk").Length != 0) _download.DownloadMagisk();
        }

        public void JarDecompile(string jarAddress)
        {
            string fileName = Path.GetFileName(jarAddress);
            if (!Directory.Exists("bin") || !File.Exists("bin\\apktool.jar")) return;
            _mainForm.StatusUpdate("Decompiling..");
            StartProcess("java.exe", "-Xms1024m -Xmx1024m -jar bin\\apktool.jar d \"" + jarAddress + "\" -o tmp -f");
            if (_hasBeenDeodexed)
            {
                if (!File.Exists("classes.dex"))
                {
                    _mainForm.DebugUpdate("\n!!! ERROR: Deodex failed - classes.dex not found.");
                    _mainForm.StatusUpdate("ERROR..");
                    return;
                }
                if (Directory.Exists("tmp")) File.Move("classes.dex", "tmp\\classes.dex");
            }
            if (!_hasBeenDeodexed)
            {
                _mainForm.StatusUpdate("Patching..");
                PatchFiles(fileName, jarAddress);
            }
            else if (_hasBeenDeodexed) JarCompile("tmp\\dist\\" + fileName, "tmp");
            _hasBeenDeodexed = false;
        }

        public void JarCompile(string outputFile, string sourceDirectory)
        {
            if (!Directory.Exists("bin") || !File.Exists("bin\\apktool.jar")) return;
            string fileName = Path.GetFileName(outputFile);
            _mainForm.StatusUpdate("Recompiling..");
            StartProcess("java.exe",
                "-Xms1024m -Xmx1024m -jar bin\\apktool.jar b -o " + outputFile + " " + sourceDirectory);
            if (!File.Exists("tmp\\dist\\" + fileName))
            {
                _mainForm.DebugUpdate("\n!!! ERROR: Compile failed - " + fileName + " not found.");
                _mainForm.StatusUpdate("ERROR..");
                return;
            }
            if (GetPatchStatus("Signature spoofing") && GetPatchTargetFile("Signature spoofing") == fileName)
            {
                if (_dexPatcherCoreRequired)
                {
                    DexPatcher("tmp\\dist\\services.jar", _dexPatcherTarget);
                    DexPatcher("tmp\\dist\\services.jar", "bin\\sigspoof_core.dex");
                }
                else
                    _mainForm.DebugUpdate("\n==> Signature spoofing patch already enabled.");
            }
            if (!Directory.Exists("apk")) Directory.CreateDirectory("apk");
            if (File.Exists("tmp\\dist\\" + fileName)) File.Move("tmp\\dist\\" + fileName, "apk\\" + fileName);
        }

        public void OdexDeodex(string odexPath, string targetJarPath, string frameworkPath, string api)
        {
            if (!Directory.Exists("bin") || !File.Exists("bin\\baksmali.jar")) return;
            string fileName = Path.GetFileName(odexPath);
            if (!Directory.Exists(frameworkPath))
            {
                _mainForm.StatusUpdate("!!! ERROR: Framework directory missing..");
                _mainForm.StatusUpdate("ERROR..");
                return;
            }
            if (Directory.Exists("smali")) Directory.Delete("smali", true);
            if (!Directory.Exists("smali")) Directory.CreateDirectory("smali");
            _mainForm.StatusUpdate("Deodexing..");
            _hasBeenDeodexed = true;
            if (api != "00" && odexPath.Contains(".odex"))
                StartProcess("java.exe",
                    "-Xms1024m -Xmx1024m -jar bin\\baksmali.jar x \"" + Path.GetFullPath(odexPath) + "\" -a " + api +
                    " -d \"" + Path.GetFullPath(frameworkPath) + "\" -o smali");
            else if (api == "00" && odexPath.Contains(".odex"))
                StartProcess("java.exe",
                    "-Xms1024m -Xmx1024m -jar bin\\baksmali.jar x \"" + Path.GetFullPath(odexPath) + "\" -d \"" +
                    Path.GetFullPath(frameworkPath) + "\" -o smali");
            else if (odexPath.Contains(".oat"))
                StartProcess("java.exe",
                    "-Xms1024m -Xmx1024m -jar bin\\baksmali.jar x \"" + Path.GetFullPath(odexPath) + "\" -o smali");
            _mainForm.DebugUpdate("\n==> Deodexed " + fileName);
            _mainForm.StatusUpdate("Patching..");
            PatchFiles(Path.GetFileName(targetJarPath), frameworkPath);
        }

        public void OdexCompile(string targetFileNoExt, string basePath)
        {
            if (!Directory.Exists("bin") || !File.Exists("bin\\smali.jar")) return;
            _mainForm.StatusUpdate("Generating classes..");
            if (Directory.Exists("smali"))
            {
                StartProcess("java.exe", "-Xms1024m -Xmx1024m -jar bin\\smali.jar a --verbose smali -o classes.dex");
                if (File.Exists("classes.dex"))
                {
                    _mainForm.DebugUpdate("\n==> Generated classes.dex");
                    if (!File.Exists(basePath + "\\" + targetFileNoExt + ".jar"))
                    {
                        _mainForm.StatusUpdate("!!! ERROR: Deodex target file missing..");
                        _mainForm.StatusUpdate("ERROR..");
                    }
                    else
                        JarDecompile(basePath + "\\" + targetFileNoExt + ".jar");
                }
                else
                {
                    _mainForm.StatusUpdate("!!! ERROR: Generating classes.dex failed..");
                    _mainForm.StatusUpdate("ERROR..");
                }
            }
            else
            {
                _mainForm.StatusUpdate("!!! ERROR: Smali directory missing..");
                _mainForm.StatusUpdate("ERROR..");
            }
        }

        public void DexPatcher(string jar, string dexPatch)
        {
            if (Directory.Exists("bin") && File.Exists("bin\\dexpatcher.jar"))
            {
                if (Directory.Exists("classes")) Directory.Delete("classes", true);
                if (!Directory.Exists("classes")) Directory.CreateDirectory("classes");
                _mainForm.StatusUpdate("Patching classes..");
                StartProcess("java.exe",
                    "-Xms1024m -Xmx1024m -jar bin\\dexpatcher.jar --multi-dex --output classes " + jar + " " +
                    dexPatch);
                using (ZipFile val = ZipFile.Read(jar))
                {
                    string[] files = Directory.GetFiles("classes", "classes*");
                    for (int i = 0; i < files.Length; i++)
                    {
                        if (val.ContainsEntry(Path.GetFileName(files[i]))) val.RemoveEntry(Path.GetFileName(files[i]));
                        val.AddFile(files[i], "/");
                    }
                    val.CompressionLevel = CompressionLevel.None;
                    val.Save();
                }
                if (Directory.Exists("classes")) Directory.Delete("classes", true);
                _mainForm.DebugUpdate("\n==> Merged patch: " + Path.GetFileNameWithoutExtension(dexPatch));
            }
        }

        public void VdexExtract(string vdexAddress, string jarFile)
        {
            if (!Directory.Exists("bin") || !File.Exists("bin\\vdexExtractor.exe")) return;
            Path.GetFileName(vdexAddress);
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(vdexAddress);
            if (fileNameWithoutExtension.Length > 5 && fileNameWithoutExtension.Substring(0, 5) == "boot-")
                fileNameWithoutExtension = fileNameWithoutExtension.Substring(5);
            _mainForm.StatusUpdate("Extracting vdex..");
            File.Copy(Path.GetFullPath(vdexAddress), "bin\\" + Path.GetFileName(vdexAddress), true);
            StartProcess("bin\\vdexExtractor.exe",
                "-i \"bin\\" + Path.GetFileName(vdexAddress) + "\" --ignore-crc-error");
            File.Delete("bin\\" + Path.GetFileName(vdexAddress));
            if (File.Exists("bin\\" + Path.GetFileNameWithoutExtension(vdexAddress) + ".apk_classes.dex"))
            {
                if (File.Exists("bin\\classes.dex")) File.Delete("bin\\classes.dex");
                File.Move("bin\\" + Path.GetFileNameWithoutExtension(vdexAddress) + ".apk_classes.dex",
                    "bin\\classes.dex");
            }
            string[] files = Directory.GetFiles("bin",
                Path.GetFileNameWithoutExtension(vdexAddress) + ".apk__classes*.dex");
            for (int i = 0; i < files.Length; i++)
            {
                if (File.Exists("bin\\classes" + (i + 2) + ".dex")) File.Delete("bin\\classes" + (i + 2) + ".dex");
                File.Move(files[i], "bin\\classes" + (i + 2) + ".dex");
            }
            if (File.Exists("bin\\" + Path.GetFileNameWithoutExtension(vdexAddress) + "_classes.dex"))
            {
                if (File.Exists("bin\\classes.dex")) File.Delete("bin\\classes.dex");
                File.Move("bin\\" + Path.GetFileNameWithoutExtension(vdexAddress) + "_classes.dex", "bin\\classes.dex");
            }
            string[] files2 =
                Directory.GetFiles("bin", Path.GetFileNameWithoutExtension(vdexAddress) + "_classes*.dex");
            for (int j = 0; j < files2.Length; j++)
            {
                if (File.Exists("bin\\classes" + (j + 2) + ".dex")) File.Delete("bin\\classes" + (j + 2) + ".dex");
                File.Move(files2[j], "bin\\classes" + (j + 2) + ".dex");
            }
            if (File.Exists("bin\\" + Path.GetFileNameWithoutExtension(vdexAddress) + "_classes.cdex"))
            {
                if (File.Exists("bin\\classes.cdex")) File.Delete("bin\\classes.cdex");
                File.Move("bin\\" + Path.GetFileNameWithoutExtension(vdexAddress) + "_classes.cdex",
                    "bin\\classes.cdex");
            }
            string[] files3 =
                Directory.GetFiles("bin", Path.GetFileNameWithoutExtension(vdexAddress) + "_classes*.cdex");
            for (int k = 0; k < files3.Length; k++)
            {
                if (File.Exists("bin\\classes" + (k + 2) + ".cdex")) File.Delete("bin\\classes" + (k + 2) + ".cdex");
                File.Move(files3[k], "bin\\classes" + (k + 2) + ".cdex");
            }
            if (File.Exists("bin\\classes.cdex"))
            {
                _mainForm.DebugUpdate("\n==> Extracting classes.cdex");
                CdexToDex("bin\\classes.cdex");
                if (!File.Exists("bin\\classes.dex"))
                {
                    _mainForm.DebugUpdate("\n!!! ERROR: Failed extracting classes.cdex");
                    _mainForm.StatusUpdate("ERROR..");
                    return;
                }
            }
            string[] files4 = Directory.GetFiles("bin", "classes*.cdex");
            for (int l = 0; l < files4.Length; l++)
            {
                _mainForm.DebugUpdate("\n==> Extracting " + Path.GetFileName(files4[l]));
                CdexToDex(files4[l]);
                if (!File.Exists("bin\\" + Path.GetFileNameWithoutExtension(files4[l]) + ".dex"))
                {
                    _mainForm.DebugUpdate("\n!!! ERROR: Failed extracting " + Path.GetFileName(files4[l]));
                    _mainForm.StatusUpdate("ERROR..");
                    return;
                }
            }
            using (ZipFile val = ZipFile.Read(jarFile))
            {
                string[] files5 = Directory.GetFiles("bin", "classes*.dex");
                for (int m = 0; m < files5.Length; m++)
                {
                    if (val.ContainsEntry(Path.GetFileName(files5[m]))) val.RemoveEntry(Path.GetFileName(files5[m]));
                    val.AddFile(files5[m], "/");
                }
                val.CompressionLevel = CompressionLevel.None;
                val.Save();
            }
            if (File.Exists("bin\\classes.dex"))
            {
                _mainForm.DebugUpdate("\n==> Extracted classes.dex");
                File.Delete("bin\\classes.dex");
            }
            string[] files6 = Directory.GetFiles("bin", "classes*.dex");
            for (int n = 0; n < files6.Length; n++)
            {
                _mainForm.DebugUpdate("\n==> Extracted " + Path.GetFileName(files6[n]));
                File.Delete("bin\\" + Path.GetFileName(files6[n]));
            }
            JarDecompile(jarFile);
        }

        private void CdexToDex(string cdexAddress)
        {
            if (_adb == null)
            {
                _adb = new Adb();
                _adb.Init(_mainForm);
            }
            if (Directory.Exists("bin") && File.Exists("bin\\compact_dex_converter"))
            {
                _adb.Push("bin\\compact_dex_converter", "/data/local/tmp/", true);
                _adb.Shell("chmod 777 /data/local/tmp/compact_dex_converter", true);
                _adb.Push(cdexAddress, "/data/local/tmp/", true);
                _adb.Shell("/data/local/tmp/compact_dex_converter /data/local/tmp/" + Path.GetFileName(cdexAddress),
                    true);
                _adb.Pull("/data/local/tmp/" + Path.GetFileName(cdexAddress) + ".new", "bin", true);
                _adb.Shell("rm -f /data/local/tmp/compact_dex_converter", true);
                _adb.Shell("rm -f /data/local/tmp/" + Path.GetFileName(cdexAddress), true);
                _adb.Shell("rm -f /data/local/tmp/" + Path.GetFileName(cdexAddress) + ".new", true);
                if (File.Exists(cdexAddress)) File.Delete(cdexAddress);
                if (File.Exists("bin\\" + Path.GetFileNameWithoutExtension(cdexAddress) + ".dex"))
                    File.Delete("bin\\" + Path.GetFileNameWithoutExtension(cdexAddress) + ".dex");
                if (File.Exists("bin\\" + Path.GetFileName(cdexAddress) + ".new"))
                    File.Move("bin\\" + Path.GetFileName(cdexAddress) + ".new",
                        "bin\\" + Path.GetFileNameWithoutExtension(cdexAddress) + ".dex");
            }
            else
            {
                _mainForm.DebugUpdate("\n!!! ERROR: compact_dex_converter missing..");
                _mainForm.StatusUpdate("ERROR..");
            }
        }

        private void PatchFiles(string targetFile, string targetFilePath)
        {
            string text = "";
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(targetFile);
            if (GetPatchStatus("Mock locations") &&
                GetPatchTargetFile("Mock locations") == fileNameWithoutExtension + ".jar")
            {
                text = GetPath("com\\android\\server\\LocationManagerService.smali");
                if (File.Exists(text + "com\\android\\server\\LocationManagerService.smali"))
                {
                    using (StreamReader streamReader =
                        File.OpenText(text + "com\\android\\server\\LocationManagerService.smali"))
                    using (StreamWriter streamWriter =
                        new StreamWriter(text + "com\\android\\server\\LocationManagerService.smali.new"))
                    {
                        string text2 = "";
                        string text3;
                        while ((text3 = streamReader.ReadLine()) != null)
                        {
                            text2 = text2 + text3 + "\n";
                            if (text3.Contains("setIsFromMockProvider"))
                            {
                                int num = text2.LastIndexOf("setIsFromMockProvider");
                                while (text2.Substring(num - 1, 2) != "0x") num--;
                                text2.Substring(num - 1, 3);
                                _mainForm.DebugUpdate("\n==> Patched mock location boolean");
                                text2 = text2.Substring(0, num - 1) + "0x0\n" + text2.Substring(num + 3);
                            }
                        }
                        streamWriter.Write(text2);
                    }
                    File.Replace(text + "com\\android\\server\\LocationManagerService.smali.new",
                        text + "com\\android\\server\\LocationManagerService.smali", null);
                }
                text = GetPath("com\\android\\server\\location\\MockProvider.smali");
                if (File.Exists(text + "com\\android\\server\\location\\MockProvider.smali"))
                {
                    using (StreamReader streamReader2 =
                        File.OpenText(text + "com\\android\\server\\location\\MockProvider.smali"))
                    using (StreamWriter streamWriter2 =
                        new StreamWriter(text + "com\\android\\server\\location\\MockProvider.smali.new"))
                    {
                        string text4 = "";
                        string text5;
                        while ((text5 = streamReader2.ReadLine()) != null)
                        {
                            text4 = text4 + text5 + "\n";
                            if (text5.Contains("setIsFromMockProvider"))
                            {
                                int num2 = text4.LastIndexOf("setIsFromMockProvider");
                                while (text4.Substring(num2 - 1, 2) != "0x") num2--;
                                text4.Substring(num2 - 1, 3);
                                _mainForm.DebugUpdate("\n==> Patched mock location boolean");
                                text4 = text4.Substring(0, num2 - 1) + "0x0\n" + text4.Substring(num2 + 3);
                            }
                        }
                        streamWriter2.Write(text4);
                    }
                    File.Replace(text + "com\\android\\server\\location\\MockProvider.smali.new",
                        text + "com\\android\\server\\location\\MockProvider.smali", null);
                }
            }
            int result;
            if (GetPatchStatus("Mock providers") &&
                GetPatchTargetFile("Mock providers") == fileNameWithoutExtension + ".jar")
            {
                text = GetPath("com\\android\\server\\LocationManagerService.smali");
                if (File.Exists(text + "com\\android\\server\\LocationManagerService.smali"))
                {
                    string text6 = File.ReadAllText(text + "com\\android\\server\\LocationManagerService.smali");
                    using (StreamWriter streamWriter3 =
                        new StreamWriter(text + "com\\android\\server\\LocationManagerService.smali.new"))
                    {
                        if (text6.Contains(".method private canCallerAccessMockLocation"))
                        {
                            int i = text6.LastIndexOf(".method private canCallerAccessMockLocation(");
                            while (text6.Substring(i, 7) != ".locals" && text6.Substring(i, 10) != ".registers") i++;
                            if (text6.Substring(i, 7) == ".locals") i += 8;
                            if (text6.Substring(i, 10) == ".registers") i += 11;
                            while (int.TryParse(text6.Substring(i, 1), out result)) i++;
                            int j = i;
                            while (text6.Substring(j, 11) != ".end method") j++;
                            text6 = text6.Substring(0, i) + "\n\n    const/4 v0, 0x1\n\n    return v0\n\n" +
                                    text6.Substring(j);
                            _mainForm.DebugUpdate("\n==> Patched mock providers function");
                        }
                        streamWriter3.Write(text6);
                    }
                    File.Replace(text + "com\\android\\server\\LocationManagerService.smali.new",
                        text + "com\\android\\server\\LocationManagerService.smali", null);
                }
                text = GetPath("com\\android\\server\\location\\AppOpsHelper.smali");
                if (File.Exists(text + "com\\android\\server\\location\\AppOpsHelper.smali"))
                {
                    string text7 = File.ReadAllText(text + "com\\android\\server\\location\\AppOpsHelper.smali");
                    using (StreamWriter streamWriter4 =
                        new StreamWriter(text + "com\\android\\server\\location\\AppOpsHelper.smali.new"))
                    {
                        if (text7.Contains(".method public noteMockLocationAccess"))
                        {
                            int k;
                            for (k = text7.LastIndexOf(".method public noteMockLocationAccess(");
                                text7.Substring(k, 7) != ".locals" && text7.Substring(k, 10) != ".registers";
                                k++)
                            {
                            }
                            if (text7.Substring(k, 7) == ".locals") k += 8;
                            if (text7.Substring(k, 10) == ".registers") k += 11;
                            while (int.TryParse(text7.Substring(k, 1), out result)) k++;
                            int l = k;
                            while (text7.Substring(l, 11) != ".end method") l++;
                            text7 = text7.Substring(0, k) + "\n\n    const/4 v0, 0x1\n\n    return v0\n\n" +
                                    text7.Substring(l);
                            _mainForm.DebugUpdate("\n==> Patched mock providers function");
                        }
                        streamWriter4.Write(text7);
                    }
                    File.Replace(text + "com\\android\\server\\location\\AppOpsHelper.smali.new",
                        text + "com\\android\\server\\location\\AppOpsHelper.smali", null);
                }
            }
            if (GetPatchStatus("GNSS updates") &&
                GetPatchTargetFile("GNSS updates") == fileNameWithoutExtension + ".jar")
            {
                text = GetPath("com\\android\\server\\location\\GnssLocationProvider.smali");
                if (File.Exists(text + "com\\android\\server\\location\\GnssLocationProvider.smali"))
                {
                    string text8 =
                        File.ReadAllText(text + "com\\android\\server\\location\\GnssLocationProvider.smali");
                    using (StreamWriter streamWriter5 =
                        new StreamWriter(text + "com\\android\\server\\location\\GnssLocationProvider.smali.new"))
                    {
                        if (text8.Contains(".method private reportLocation"))
                        {
                            int m = text8.LastIndexOf(".method private reportLocation(");
                            while (text8.Substring(m, 7) != ".locals" && text8.Substring(m, 10) != ".registers") m++;
                            if (text8.Substring(m, 7) == ".locals") m += 8;
                            if (text8.Substring(m, 10) == ".registers") m += 11;
                            while (int.TryParse(text8.Substring(m, 1), out result)) m++;
                            int n = m;
                            while (text8.Substring(n, 11) != ".end method") n++;
                            text8 = text8.Substring(0, m) + "\n\n    return-void\n\n" + text8.Substring(n);
                            _mainForm.DebugUpdate("\n==> Patched gnss updates function");
                        }
                        streamWriter5.Write(text8);
                    }
                    File.Replace(text + "com\\android\\server\\location\\GnssLocationProvider.smali.new",
                        text + "com\\android\\server\\location\\GnssLocationProvider.smali", null);
                }
                text = GetPath("com\\android\\server\\location\\gnss\\GnssLocationProvider.smali");
                if (File.Exists(text + "com\\android\\server\\location\\gnss\\GnssLocationProvider.smali"))
                {
                    string text9 =
                        File.ReadAllText(text + "com\\android\\server\\location\\gnss\\GnssLocationProvider.smali");
                    using (StreamWriter streamWriter6 =
                        new StreamWriter(text + "com\\android\\server\\location\\gnss\\GnssLocationProvider.smali.new"))
                    {
                        if (text9.Contains(".method private reportLocation"))
                        {
                            int num3 = text9.LastIndexOf(".method private reportLocation(");
                            while (text9.Substring(num3, 7) != ".locals" && text9.Substring(num3, 10) != ".registers") num3++;
                            if (text9.Substring(num3, 7) == ".locals") num3 += 8;
                            if (text9.Substring(num3, 10) == ".registers") num3 += 11;
                            while (int.TryParse(text9.Substring(num3, 1), out result)) num3++;
                            int num4 = num3;
                            while (text9.Substring(num4, 11) != ".end method") num4++;
                            text9 = text9.Substring(0, num3) + "\n\n    return-void\n\n" + text9.Substring(num4);
                            _mainForm.DebugUpdate("\n==> Patched gnss updates function");
                        }
                        streamWriter6.Write(text9);
                    }
                    File.Replace(text + "com\\android\\server\\location\\gnss\\GnssLocationProvider.smali.new",
                        text + "com\\android\\server\\location\\gnss\\GnssLocationProvider.smali", null);
                }
            }
            if (GetPatchStatus("Secure flag") && GetPatchTargetFile("Secure flag") == fileNameWithoutExtension + ".jar")
            {
                text = GetPath("com\\android\\server\\wm\\WindowManagerService.smali");
                if (File.Exists(text + "com\\android\\server\\wm\\WindowManagerService.smali"))
                {
                    string text10 = File.ReadAllText(text + "com\\android\\server\\wm\\WindowManagerService.smali");
                    using (StreamWriter streamWriter7 =
                        new StreamWriter(text + "com\\android\\server\\wm\\WindowManagerService.smali.new"))
                    {
                        if (text10.Contains(".method isSecureLocked"))
                        {
                            int num5 = text10.LastIndexOf(".method isSecureLocked(");
                            while (text10.Substring(num5, 7) != ".locals" && text10.Substring(num5, 10) != ".registers")
                                num5++;
                            if (text10.Substring(num5, 7) == ".locals") num5 += 8;
                            if (text10.Substring(num5, 10) == ".registers") num5 += 11;
                            while (int.TryParse(text10.Substring(num5, 1), out result)) num5++;
                            int num6 = num5;
                            while (text10.Substring(num6, 11) != ".end method") num6++;
                            text10 = text10.Substring(0, num5) + "\n\n    const/4 v0, 0x0\n\n    return v0\n\n" +
                                     text10.Substring(num6);
                            _mainForm.DebugUpdate("\n==> Patched secure flag function");
                        }
                        if (text10.Contains("setScreenCaptureDisabled("))
                        {
                            int num7 = text10.LastIndexOf("setScreenCaptureDisabled(");
                            while (text10.Substring(num7, 17) != "SparseArray;->put") num7++;
                            while (text10.Substring(num7, 1) != "v") num7--;
                            string text11 = text10.Substring(num7, 2);
                            while (text10.Substring(num7, 14) != "invoke-virtual") num7--;
                            text10 = text10.Substring(0, num7) + "const/4 " + text11 + ", 0x0\n\n    " +
                                     text10.Substring(num7);
                            _mainForm.DebugUpdate("\n==> Patched screen capture boolean");
                        }
                        streamWriter7.Write(text10);
                    }
                    File.Replace(text + "com\\android\\server\\wm\\WindowManagerService.smali.new",
                        text + "com\\android\\server\\wm\\WindowManagerService.smali", null);
                }
                text = GetPath("com\\android\\server\\wm\\WindowState.smali");
                if (File.Exists(text + "com\\android\\server\\wm\\WindowState.smali"))
                {
                    string text12 = File.ReadAllText(text + "com\\android\\server\\wm\\WindowState.smali");
                    using (StreamWriter streamWriter8 =
                        new StreamWriter(text + "com\\android\\server\\wm\\WindowState.smali.new"))
                    {
                        if (text12.Contains(".method isSecureLocked"))
                        {
                            int num8;
                            for (num8 = text12.LastIndexOf(".method isSecureLocked(");
                                text12.Substring(num8, 7) != ".locals" && text12.Substring(num8, 10) != ".registers";
                                num8++)
                            {
                            }
                            if (text12.Substring(num8, 7) == ".locals") num8 += 8;
                            if (text12.Substring(num8, 10) == ".registers") num8 += 11;
                            for (; int.TryParse(text12.Substring(num8, 1), out result); num8++)
                            {
                            }
                            int num9;
                            for (num9 = num8; text12.Substring(num9, 11) != ".end method"; num9++)
                            {
                            }
                            text12 = text12.Substring(0, num8) + "\n\n    const/4 v0, 0x0\n\n    return v0\n\n" +
                                     text12.Substring(num9);
                            _mainForm.DebugUpdate("\n==> Patched secure flag function");
                        }
                        streamWriter8.Write(text12);
                    }
                    File.Replace(text + "com\\android\\server\\wm\\WindowState.smali.new",
                        text + "com\\android\\server\\wm\\WindowState.smali", null);
                }
                text = GetPath("com\\android\\server\\wm\\ScreenshotController.smali");
                if (File.Exists(text + "com\\android\\server\\wm\\ScreenshotController.smali"))
                {
                    string text13 = File.ReadAllText(text + "com\\android\\server\\wm\\ScreenshotController.smali");
                    using (StreamWriter streamWriter9 =
                        new StreamWriter(text + "com\\android\\server\\wm\\ScreenshotController.smali.new"))
                    {
                        if (text13.Contains(".method private preventTakingScreenshotToTargetWindow"))
                        {
                            int num10 = text13.LastIndexOf(".method private preventTakingScreenshotToTargetWindow(");
                            while (text13.Substring(num10, 7) != ".locals" && text13.Substring(num10, 10) != ".registers") num10++;
                            if (text13.Substring(num10, 7) == ".locals") num10 += 8;
                            if (text13.Substring(num10, 10) == ".registers") num10 += 11;
                            while (int.TryParse(text13.Substring(num10, 1), out result)) num10++;
                            int num11 = num10;
                            while (text13.Substring(num11, 11) != ".end method") num11++;
                            text13 = text13.Substring(0, num10) + "\n\n    const/4 v0, 0x0\n\n    return v0\n\n" +
                                     text13.Substring(num11);
                            _mainForm.DebugUpdate("\n==> Patched screenshot controller");
                        }
                        streamWriter9.Write(text13);
                    }
                    File.Replace(text + "com\\android\\server\\wm\\ScreenshotController.smali.new",
                        text + "com\\android\\server\\wm\\ScreenshotController.smali", null);
                }
                text = GetPath("com\\android\\server\\wm\\WindowSurfaceController.smali");
                if (File.Exists(text + "com\\android\\server\\wm\\WindowSurfaceController.smali"))
                {
                    string text14 = File.ReadAllText(text + "com\\android\\server\\wm\\WindowSurfaceController.smali");
                    using (StreamWriter streamWriter10 =
                        new StreamWriter(text + "com\\android\\server\\wm\\WindowSurfaceController.smali.new"))
                    {
                        if (text14.Contains(".method setSecure("))
                        {
                            int num12 = text14.LastIndexOf(".method setSecure(");
                            while (text14.Substring(num12, 7) != ".locals" && text14.Substring(num12, 10) != ".registers") num12++;
                            if (text14.Substring(num12, 7) == ".locals") num12 += 8;
                            if (text14.Substring(num12, 10) == ".registers") num12 += 11;
                            while (int.TryParse(text14.Substring(num12, 1), out result)) num12++;
                            int num13 = num12;
                            while (text14.Substring(num13, 11) != ".end method") num13++;
                            text14 = text14.Substring(0, num12) + "\n\n    return-void\n\n" + text14.Substring(num13);
                            _mainForm.DebugUpdate("\n==> Patched set secure function");
                        }
                        streamWriter10.Write(text14);
                    }
                    File.Replace(text + "com\\android\\server\\wm\\WindowSurfaceController.smali.new",
                        text + "com\\android\\server\\wm\\WindowSurfaceController.smali", null);
                }
                text = GetPath("com\\android\\server\\devicepolicy\\DevicePolicyManagerService.smali");
                if (File.Exists(text + "com\\android\\server\\devicepolicy\\DevicePolicyManagerService.smali"))
                {
                    string text15 =
                        File.ReadAllText(text + "com\\android\\server\\devicepolicy\\DevicePolicyManagerService.smali");
                    using (StreamWriter streamWriter11 =
                        new StreamWriter(text +
                                         "com\\android\\server\\devicepolicy\\DevicePolicyManagerService.smali.new"))
                    {
                        if (text15.Contains(".method public setScreenCaptureDisabled("))
                        {
                            int num14 = text15.LastIndexOf(".method public setScreenCaptureDisabled(");
                            while (text15.Substring(num14, 7) != ".locals" && text15.Substring(num14, 10) != ".registers") num14++;
                            if (text15.Substring(num14, 7) == ".locals") num14 += 8;
                            if (text15.Substring(num14, 10) == ".registers") num14 += 11;
                            while (int.TryParse(text15.Substring(num14, 1), out result)) num14++;
                            int num15 = num14;
                            while (text15.Substring(num15, 11) != ".end method") num15++;
                            text15 = text15.Substring(0, num14) + "\n\n    return-void\n\n" + text15.Substring(num15);
                            _mainForm.DebugUpdate("\n==> Patched capture function");
                        }
                        if (text15.Contains(".method public getScreenCaptureDisabled("))
                        {
                            int num16 = text15.LastIndexOf(".method public getScreenCaptureDisabled(");
                            while (text15.Substring(num16, 7) != ".locals" && text15.Substring(num16, 10) != ".registers")
                                num16++;
                            if (text15.Substring(num16, 7) == ".locals") num16 += 8;
                            if (text15.Substring(num16, 10) == ".registers") num16 += 11;
                            while (int.TryParse(text15.Substring(num16, 1), out result)) num16++;
                            int num17 = num16;
                            while (text15.Substring(num17, 11) != ".end method") num17++;
                            text15 = text15.Substring(0, num16) + "\n\n    const/4 v0, 0x1\n\n    return v0\n\n" +
                                     text15.Substring(num17);
                            _mainForm.DebugUpdate("\n==> Patched get capture function");
                        }
                        streamWriter11.Write(text15);
                    }
                    File.Replace(text + "com\\android\\server\\devicepolicy\\DevicePolicyManagerService.smali.new",
                        text + "com\\android\\server\\devicepolicy\\DevicePolicyManagerService.smali", null);
                }
                text = GetPath("com\\android\\server\\devicepolicy\\DevicePolicyCacheImpl.smali");
                if (File.Exists(text + "com\\android\\server\\devicepolicy\\DevicePolicyCacheImpl.smali"))
                {
                    string text16 =
                        File.ReadAllText(text + "com\\android\\server\\devicepolicy\\DevicePolicyCacheImpl.smali");
                    using (StreamWriter streamWriter12 =
                        new StreamWriter(text + "com\\android\\server\\devicepolicy\\DevicePolicyCacheImpl.smali.new"))
                    {
                        if (text16.Contains(".method public setScreenCaptureDisabled("))
                        {
                            int num18 = text16.LastIndexOf(".method public setScreenCaptureDisabled(");
                            while (text16.Substring(num18, 7) != ".locals" && text16.Substring(num18, 10) != ".registers") num18++;
                            if (text16.Substring(num18, 7) == ".locals") num18 += 8;
                            if (text16.Substring(num18, 10) == ".registers") num18 += 11;
                            while (int.TryParse(text16.Substring(num18, 1), out result)) num18++;
                            int num19 = num18;
                            while (text16.Substring(num19, 11) != ".end method") num19++;
                            text16 = text16.Substring(0, num18) + "\n\n    return-void\n\n" + text16.Substring(num19);
                            _mainForm.DebugUpdate("\n==> Patched capture function");
                        }
                        if (text16.Contains(".method public getScreenCaptureDisabled("))
                        {
                            int num20 = text16.LastIndexOf(".method public getScreenCaptureDisabled(");
                            while (text16.Substring(num20, 7) != ".locals" && text16.Substring(num20, 10) != ".registers") num20++;
                            if (text16.Substring(num20, 7) == ".locals") num20 += 8;
                            if (text16.Substring(num20, 10) == ".registers") num20 += 11;
                            while (int.TryParse(text16.Substring(num20, 1), out result)) num20++;
                            int num21 = num20;
                            while (text16.Substring(num21, 11) != ".end method") num21++;
                            text16 = text16.Substring(0, num20) + "\n\n    const/4 v0, 0x1\n\n    return v0\n\n" +
                                     text16.Substring(num21);
                            _mainForm.DebugUpdate("\n==> Patched get capture function");
                        }
                        if (text16.Contains(".method public isScreenCaptureAllowed("))
                        {
                            int num22 = text16.LastIndexOf(".method public isScreenCaptureAllowed(");
                            while (text16.Substring(num22, 7) != ".locals" && text16.Substring(num22, 10) != ".registers") num22++;
                            if (text16.Substring(num22, 7) == ".locals") num22 += 8;
                            if (text16.Substring(num22, 10) == ".registers") num22 += 11;
                            while (int.TryParse(text16.Substring(num22, 1), out result)) num22++;
                            int num23 = num22;
                            while (text16.Substring(num23, 11) != ".end method") num23++;
                            text16 = text16.Substring(0, num22) + "\n\n    const/4 v0, 0x1\n\n    return v0\n\n" +
                                     text16.Substring(num23);
                            _mainForm.DebugUpdate("\n==> Patched is capture function");
                        }
                        streamWriter12.Write(text16);
                    }
                    File.Replace(text + "com\\android\\server\\devicepolicy\\DevicePolicyCacheImpl.smali.new",
                        text + "com\\android\\server\\devicepolicy\\DevicePolicyCacheImpl.smali", null);
                }
            }
            if (GetPatchStatus("Signature verification") &&
                GetPatchTargetFile("Signature verification") == fileNameWithoutExtension + ".jar")
            {
                text = GetPath("com\\android\\server\\pm\\PackageManagerService.smali");
                if (File.Exists(text + "com\\android\\server\\pm\\PackageManagerService.smali"))
                {
                    string text17 = File.ReadAllText(text + "com\\android\\server\\pm\\PackageManagerService.smali");
                    using (StreamWriter streamWriter13 =
                        new StreamWriter(text + "com\\android\\server\\pm\\PackageManagerService.smali.new"))
                    {
                        if (text17.Contains(".method static compareSignatures("))
                        {
                            int num24;
                            for (num24 = text17.LastIndexOf(".method static compareSignatures(");
                                text17.Substring(num24, 7) != ".locals" && text17.Substring(num24, 10) != ".registers";
                                num24++)
                            {
                            }
                            if (text17.Substring(num24, 7) == ".locals") num24 += 8;
                            if (text17.Substring(num24, 10) == ".registers") num24 += 11;
                            for (; int.TryParse(text17.Substring(num24, 1), out result); num24++)
                            {
                            }
                            int num25;
                            for (num25 = num24; text17.Substring(num25, 11) != ".end method"; num25++)
                            {
                            }
                            text17 = text17.Substring(0, num24) + "\n\n    const/4 v0, 0x0\n\n    return v0\n\n" +
                                     text17.Substring(num25);
                            _mainForm.DebugUpdate("\n==> Patched signature verification function");
                        }
                        if (text17.Contains(".method public checkSignatures("))
                        {
                            int num26;
                            for (num26 = text17.LastIndexOf(".method public checkSignatures(");
                                text17.Substring(num26, 7) != ".locals" && text17.Substring(num26, 10) != ".registers";
                                num26++)
                            {
                            }
                            if (text17.Substring(num26, 7) == ".locals") num26 += 8;
                            if (text17.Substring(num26, 10) == ".registers") num26 += 11;
                            for (; int.TryParse(text17.Substring(num26, 1), out result); num26++)
                            {
                            }
                            int num27;
                            for (num27 = num26; text17.Substring(num27, 11) != ".end method"; num27++)
                            {
                            }
                            text17 = text17.Substring(0, num26) + "\n\n    const/4 v0, 0x0\n\n    return v0\n\n" +
                                     text17.Substring(num27);
                            _mainForm.DebugUpdate("\n==> Patched signature verification function");
                        }
                        streamWriter13.Write(text17);
                    }
                    File.Replace(text + "com\\android\\server\\pm\\PackageManagerService.smali.new",
                        text + "com\\android\\server\\pm\\PackageManagerService.smali", null);
                }
                text = GetPath("com\\android\\server\\pm\\PackageManagerServiceUtils.smali");
                if (File.Exists(text + "com\\android\\server\\pm\\PackageManagerServiceUtils.smali"))
                {
                    string text18 =
                        File.ReadAllText(text + "com\\android\\server\\pm\\PackageManagerServiceUtils.smali");
                    using (StreamWriter streamWriter14 =
                        new StreamWriter(text + "com\\android\\server\\pm\\PackageManagerServiceUtils.smali.new"))
                    {
                        if (text18.Contains(".method public static compareSignatures("))
                        {
                            int num28;
                            for (num28 = text18.LastIndexOf(".method public static compareSignatures(");
                                text18.Substring(num28, 7) != ".locals" && text18.Substring(num28, 10) != ".registers";
                                num28++)
                            {
                            }
                            if (text18.Substring(num28, 7) == ".locals") num28 += 8;
                            if (text18.Substring(num28, 10) == ".registers") num28 += 11;
                            for (; int.TryParse(text18.Substring(num28, 1), out result); num28++)
                            {
                            }
                            int num29;
                            for (num29 = num28; text18.Substring(num29, 11) != ".end method"; num29++)
                            {
                            }
                            text18 = text18.Substring(0, num28) + "\n\n    const/4 v0, 0x0\n\n    return v0\n\n" +
                                     text18.Substring(num29);
                            _mainForm.DebugUpdate("\n==> Patched signature verification util function");
                        }
                        streamWriter14.Write(text18);
                    }
                    File.Replace(text + "com\\android\\server\\pm\\PackageManagerServiceUtils.smali.new",
                        text + "com\\android\\server\\pm\\PackageManagerServiceUtils.smali", null);
                }
            }
            if (GetPatchStatus("Signature spoofing") &&
                GetPatchTargetFile("Signature spoofing") == fileNameWithoutExtension + ".jar")
            {
                text = GetPath("com\\android\\server\\pm\\PackageManagerService.smali");
                if (File.Exists(text + "com\\android\\server\\pm\\PackageManagerService.smali"))
                {
                    string text19 = File.ReadAllText(text + "com\\android\\server\\pm\\PackageManagerService.smali");
                    if (text19.Contains(".method private generatePackageInfo(") ||
                        text19.Contains(".method generatePackageInfo("))
                    {
                        int num30 = text19.LastIndexOf(".method private generatePackageInfo(");
                        if (num30 == -1) num30 = text19.LastIndexOf(".method generatePackageInfo(");
                        int num31;
                        for (num31 = num30; text19.Substring(num31, 2) != ";\r"; num31 += 2)
                        {
                        }
                        num31++;
                        if (text19.Substring(num30, num31 - num30).Contains("PackageParser"))
                            _dexPatcherTarget = "bin/sigspoof_4.1-6.0.dex";
                        else if (text19.Substring(num30, num31 - num30).Contains("PackageSetting"))
                            _dexPatcherTarget = "bin/sigspoof_7.0-9.0.dex";
                    }
                }
                else if (!File.Exists(text + "com\\android\\server\\pm\\PackageManagerService.smali"))
                {
                    _mainForm.DebugUpdate("\n!!! ERROR: Signature spoof class not found.");
                    _mainForm.StatusUpdate("ERROR..");
                    return;
                }
                if (!File.Exists(GetPath("com\\android\\server\\pm\\GeneratePackageInfoHook.smali") +
                                 "com\\android\\server\\pm\\GeneratePackageInfoHook.smali"))
                    _dexPatcherCoreRequired = true;
            }
            if (GetPatchStatus("Recovery reboot") &&
                GetPatchTargetFile("Recovery reboot") == fileNameWithoutExtension + ".jar")
            {
                text = GetPath("com\\android\\server\\statusbar\\StatusBarManagerService.smali");
                if (!File.Exists(text + "com\\android\\server\\statusbar\\StatusBarManagerService.smali"))
                {
                    _mainForm.DebugUpdate("\n!!! ERROR: Reboot behaviour status class not found.");
                    _mainForm.StatusUpdate("ERROR..");
                    return;
                }
                string text20 =
                    File.ReadAllText(text + "com\\android\\server\\statusbar\\StatusBarManagerService.smali");
                using (StreamWriter streamWriter15 =
                    new StreamWriter(text + "com\\android\\server\\statusbar\\StatusBarManagerService.smali.new"))
                {
                    if (text20.Contains("lambda$reboot$"))
                    {
                        int num32;
                        for (num32 = text20.IndexOf("lambda$reboot$");
                            text20.Substring(num32, 23) != "const-string/jumbo v2, ";
                            num32++)
                        {
                        }
                        num32 += 24;
                        int num33;
                        for (num33 = num32; text20.Substring(num33, 1) != "\""; num33++)
                        {
                        }
                        text20 = text20.Substring(0, num32) + "recovery" + text20.Substring(num33);
                        _mainForm.DebugUpdate("\n==> Patched recovery reboot function");
                    }
                    streamWriter15.Write(text20);
                }
                File.Replace(text + "com\\android\\server\\statusbar\\StatusBarManagerService.smali.new",
                    text + "com\\android\\server\\statusbar\\StatusBarManagerService.smali", null);
                text = GetPath("com\\android\\server\\wm\\WindowManagerService.smali");
                if (!File.Exists(text + "com\\android\\server\\wm\\WindowManagerService.smali"))
                {
                    _mainForm.DebugUpdate("\n!!! ERROR: Reboot behaviour window class not found.");
                    _mainForm.StatusUpdate("ERROR..");
                    return;
                }
                string text21 = File.ReadAllText(text + "com\\android\\server\\wm\\WindowManagerService.smali");
                using (StreamWriter streamWriter16 =
                    new StreamWriter(text + "com\\android\\server\\wm\\WindowManagerService.smali.new"))
                {
                    if (text21.Contains("reboot(Z)V"))
                    {
                        int num34;
                        for (num34 = text21.LastIndexOf("reboot(Z)V");
                            text21.Substring(num34, 11) != ".end method";
                            num34++)
                        {
                        }
                        num34 += 11;
                        int num35 = text21.LastIndexOf("reboot(Z)V");
                        if (text21.Substring(num35, num34 - num35).Contains("const-string/jumbo v1, "))
                        {
                            for (; text21.Substring(num35, 23) != "const-string/jumbo v1, "; num35++)
                            {
                            }
                            num35 += 24;
                        }
                        if (text21.Substring(num35, num34 - num35).Contains("const-string v1, "))
                        {
                            for (; text21.Substring(num35, 17) != "const-string v1, "; num35++)
                            {
                            }
                            num35 += 18;
                        }
                        int num36;
                        for (num36 = num35; text21.Substring(num36, 1) != "\""; num36++)
                        {
                        }
                        text21 = text21.Substring(0, num35) + "recovery" + text21.Substring(num36);
                        _mainForm.DebugUpdate("\n==> Patched recovery reboot function");
                    }
                    streamWriter16.Write(text21);
                }
                File.Replace(text + "com\\android\\server\\wm\\WindowManagerService.smali.new",
                    text + "com\\android\\server\\wm\\WindowManagerService.smali", null);
            }
            if (GetPatchStatus("Samsung Knox") &&
                GetPatchTargetFile("Samsung Knox") == fileNameWithoutExtension + ".jar")
            {
                text = GetPath("com\\android\\server\\KnoxFileHandler.smali");
                if (!File.Exists(text + "com\\android\\server\\KnoxFileHandler.smali"))
                {
                    _mainForm.DebugUpdate("\n!!! ERROR: Knox patch class not found.");
                    _mainForm.StatusUpdate("ERROR..");
                    return;
                }
                string text22 = File.ReadAllText(text + "com\\android\\server\\KnoxFileHandler.smali");
                using (StreamWriter streamWriter17 =
                    new StreamWriter(text + "com\\android\\server\\KnoxFileHandler.smali.new"))
                {
                    if (text22.Contains(".method public isTimaAvailable("))
                    {
                        int num37;
                        for (num37 = text22.LastIndexOf(".method public isTimaAvailable(");
                            text22.Substring(num37, 7) != ".locals" && text22.Substring(num37, 10) != ".registers";
                            num37++)
                        {
                        }
                        if (text22.Substring(num37, 7) == ".locals") num37 += 8;
                        if (text22.Substring(num37, 10) == ".registers") num37 += 11;
                        for (; int.TryParse(text22.Substring(num37, 1), out result); num37++)
                        {
                        }
                        int num38;
                        for (num38 = num37; text22.Substring(num38, 11) != ".end method"; num38++)
                        {
                        }
                        text22 = text22.Substring(0, num37) + "\n\n    const/4 v0, 0x1\n\n    return v0\n\n" +
                                 text22.Substring(num38);
                        _mainForm.DebugUpdate("\n==> Patched knox function");
                    }
                    streamWriter17.Write(text22);
                }
                File.Replace(text + "com\\android\\server\\KnoxFileHandler.smali.new",
                    text + "com\\android\\server\\KnoxFileHandler.smali", null);
                text = GetPath("com\\android\\server\\pm\\PersonaServiceHelper.smali");
                if (!File.Exists(text + "com\\android\\server\\pm\\PersonaServiceHelper.smali"))
                {
                    _mainForm.DebugUpdate("\n!!! ERROR: Knox patch class not found.");
                    _mainForm.StatusUpdate("ERROR..");
                    return;
                }
                string text23 = File.ReadAllText(text + "com\\android\\server\\pm\\PersonaServiceHelper.smali");
                using (StreamWriter streamWriter18 =
                    new StreamWriter(text + "com\\android\\server\\pm\\PersonaServiceHelper.smali.new"))
                {
                    if (text23.Contains(".method public static isTimaAvailable("))
                    {
                        int num39;
                        for (num39 = text23.LastIndexOf(".method public static isTimaAvailable(");
                            text23.Substring(num39, 7) != ".locals" && text23.Substring(num39, 10) != ".registers";
                            num39++)
                        {
                        }
                        if (text23.Substring(num39, 7) == ".locals") num39 += 8;
                        if (text23.Substring(num39, 10) == ".registers") num39 += 11;
                        for (; int.TryParse(text23.Substring(num39, 1), out result); num39++)
                        {
                        }
                        int num40;
                        for (num40 = num39; text23.Substring(num40, 11) != ".end method"; num40++)
                        {
                        }
                        text23 = text23.Substring(0, num39) + "\n\n    const/4 v0, 0x1\n\n    return v0\n\n" +
                                 text23.Substring(num40);
                        _mainForm.DebugUpdate("\n==> Patched knox function");
                    }
                    streamWriter18.Write(text23);
                }
                File.Replace(text + "com\\android\\server\\pm\\PersonaServiceHelper.smali.new",
                    text + "com\\android\\server\\pm\\PersonaServiceHelper.smali", null);
                text = GetPath("com\\android\\server\\pm\\TimaHelper.smali");
                if (!File.Exists(text + "com\\android\\server\\pm\\TimaHelper.smali"))
                {
                    _mainForm.DebugUpdate("\n!!! ERROR: Knox patch class not found.");
                    _mainForm.StatusUpdate("ERROR..");
                    return;
                }
                string text24 = File.ReadAllText(text + "com\\android\\server\\pm\\TimaHelper.smali");
                using (StreamWriter streamWriter19 =
                    new StreamWriter(text + "com\\android\\server\\pm\\TimaHelper.smali.new"))
                {
                    if (text24.Contains(".method public isTimaAvailable("))
                    {
                        int num41;
                        for (num41 = text24.LastIndexOf(".method public isTimaAvailable(");
                            text24.Substring(num41, 7) != ".locals" && text24.Substring(num41, 10) != ".registers";
                            num41++)
                        {
                        }
                        if (text24.Substring(num41, 7) == ".locals") num41 += 8;
                        if (text24.Substring(num41, 10) == ".registers") num41 += 11;
                        for (; int.TryParse(text24.Substring(num41, 1), out result); num41++)
                        {
                        }
                        int num42;
                        for (num42 = num41; text24.Substring(num42, 11) != ".end method"; num42++)
                        {
                        }
                        text24 = text24.Substring(0, num41) + "\n\n    const/4 v0, 0x1\n\n    return v0\n\n" +
                                 text24.Substring(num42);
                        _mainForm.DebugUpdate("\n==> Patched knox function");
                    }
                    streamWriter19.Write(text24);
                }
                File.Replace(text + "com\\android\\server\\pm\\TimaHelper.smali.new",
                    text + "com\\android\\server\\pm\\TimaHelper.smali", null);
            }
            if (GetPatchStatus("High volume warning") &&
                GetPatchTargetFile("High volume warning") == fileNameWithoutExtension + ".jar")
            {
                text = GetPath("com\\android\\server\\audio\\AudioService.smali");
                if (!File.Exists(text + "com\\android\\server\\audio\\AudioService.smali"))
                {
                    _mainForm.DebugUpdate("\n!!! ERROR: High volume warning class not found.");
                    _mainForm.StatusUpdate("ERROR..");
                    return;
                }
                string text25 = File.ReadAllText(text + "com\\android\\server\\audio\\AudioService.smali");
                using (StreamWriter streamWriter20 =
                    new StreamWriter(text + "com\\android\\server\\audio\\AudioService.smali.new"))
                {
                    if (text25.Contains(".method private checkSafeMediaVolume("))
                    {
                        int num43;
                        for (num43 = text25.LastIndexOf(".method private checkSafeMediaVolume(");
                            text25.Substring(num43, 7) != ".locals" && text25.Substring(num43, 10) != ".registers";
                            num43++)
                        {
                        }
                        if (text25.Substring(num43, 7) == ".locals") num43 += 8;
                        if (text25.Substring(num43, 10) == ".registers") num43 += 11;
                        for (; int.TryParse(text25.Substring(num43, 1), out result); num43++)
                        {
                        }
                        int num44;
                        for (num44 = num43; text25.Substring(num44, 11) != ".end method"; num44++)
                        {
                        }
                        text25 = text25.Substring(0, num43) + "\n\n    const/4 v0, 0x1\n\n    return v0\n\n" +
                                 text25.Substring(num44);
                        _mainForm.DebugUpdate("\n==> Patched high volume warning function");
                    }
                    streamWriter20.Write(text25);
                }
                File.Replace(text + "com\\android\\server\\audio\\AudioService.smali.new",
                    text + "com\\android\\server\\audio\\AudioService.smali", null);
            }
            if (text == "") text = GetPath("");
            if (text.Contains("tmp"))
                JarCompile("tmp\\dist\\" + fileNameWithoutExtension + ".jar", "tmp");
            else if (text == "smali\\") OdexCompile(fileNameWithoutExtension, Path.GetDirectoryName(targetFilePath));
        }

        private bool GetPatchStatus(string patchTitle)
        {
            Patches = _mainForm.Patches;
            return Patches.Find(x => x.PatchTitle.Equals(patchTitle)).Status;
        }

        private string GetPatchTargetFile(string patchTitle)
        {
            Patches = _mainForm.Patches;
            return Patches.Find(x => x.PatchTitle.Equals(patchTitle)).TargetFile;
        }

        private string GetPath(string file)
        {
            string result = "";
            if (file == "" || file == null)
            {
                if (Directory.Exists("tmp\\smali"))
                    result = "tmp\\smali\\";
                else if (Directory.Exists("smali")) result = "smali\\";
            }
            else if (File.Exists("tmp\\" + file))
                result = "tmp\\";
            else if (File.Exists("tmp\\smali\\" + file))
                result = "tmp\\smali\\";
            else if (File.Exists("tmp\\smali_classes2\\" + file))
                result = "tmp\\smali_classes2\\";
            else if (File.Exists("tmp\\smali_classes3\\" + file))
                result = "tmp\\smali_classes3\\";
            else if (File.Exists("tmp\\smali_classes4\\" + file))
                result = "tmp\\smali_classes4\\";
            else if (File.Exists("smali\\" + file)) result = "smali\\";
            return result;
        }

        private void StartProcess(string exe, string args)
        {
            try
            {
                using (Process process = Process.Start(new ProcessStartInfo
                {
                    Arguments = args,
                    FileName = exe,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }))
                    while (!process.HasExited)
                    {
                        string str;
                        if ((str = process.StandardOutput.ReadLine()) != null ||
                            (str = process.StandardError.ReadLine()) != null) _mainForm.DebugUpdate("\n" + str);
                    }
            }
            catch (Exception ex)
            {
                _mainForm.DebugUpdate("\n!!! ERROR: " + ex.Message);
                _mainForm.StatusUpdate("ERROR..");
            }
        }
    }
}