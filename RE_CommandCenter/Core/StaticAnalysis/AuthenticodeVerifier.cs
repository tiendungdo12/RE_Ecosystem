using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace RE_CommandCenter.Core.StaticAnalysis
{
    public static class AuthenticodeVerifier
    {
        #region WinTrust P/Invoke
        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
        private static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, ref WINTRUST_DATA pWVTData);

        [DllImport("wintrust.dll", SetLastError = true)]
        private static extern bool CryptCATAdminAcquireContext(out IntPtr phCatAdmin, IntPtr pgSubsystem, int dwFlags);

        [DllImport("wintrust.dll", SetLastError = true)]
        private static extern bool CryptCATAdminCalcHashFromFileHandle(IntPtr hFile, ref uint pcbHash, byte[]? pbHash, int dwFlags);

        [DllImport("wintrust.dll", SetLastError = true)]
        private static extern IntPtr CryptCATAdminEnumCatalogFromHash(IntPtr hCatAdmin, byte[] pbHash, uint cbHash, int dwFlags, ref IntPtr phPrevCatInfo);

        [DllImport("wintrust.dll", SetLastError = true)]
        private static extern bool CryptCATCatalogInfoFromContext(IntPtr hCatInfo, ref CATALOG_INFO psCatInfo, int dwFlags);

        [DllImport("wintrust.dll", SetLastError = true)]
        private static extern bool CryptCATAdminReleaseCatalogContext(IntPtr hCatAdmin, IntPtr hCatInfo, int dwFlags);

        [DllImport("wintrust.dll", SetLastError = true)]
        private static extern bool CryptCATAdminReleaseContext(IntPtr hCatAdmin, int dwFlags);

        private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        private static readonly Guid DRIVER_ACTION_VERIFY = new Guid("F750E6C3-38EE-11d1-85E5-00C04FC295EE");

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_CATALOG_INFO
        {
            public uint cbStruct;
            public uint dwCatalogVersion;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszCatalogFilePath;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszMemberTag;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszMemberFilePath;
            public IntPtr hMemberFile;
            public IntPtr pbCalculatedFileHash;
            public uint cbCalculatedFileHash;
            public IntPtr pcCatalogContext;
            public IntPtr hCatAdmin;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pInfo; // pointer to WINTRUST_FILE_INFO or WINTRUST_CATALOG_INFO
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CATALOG_INFO
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string wszCatalogFile;
        }

        private const uint WTD_UI_NONE = 2;
        private const uint WTD_REVOKE_NONE = 0;
        private const uint WTD_CHOICE_FILE = 1;
        private const uint WTD_CHOICE_CATALOG = 2;
        private const uint WTD_STATEACTION_IGNORE = 0;
        private const uint WTD_STATEACTION_VERIFY = 1;
        private const uint WTD_STATEACTION_CLOSE = 2;
        #endregion

        public static string VerifySignature(string filePath)
        {
            string status = "UnknownError";
            string? subject = null;

            // 1. Try Embedded Signature First
            uint result = VerifyEmbedded(filePath);
            if (result == 0)
            {
                status = "Valid";
            }
            else
            {
                // 2. Try Catalog Signature if Embedded fails
                result = VerifyCatalog(filePath);
                if (result == 0)
                {
                    status = "Valid";
                }
                else if (result == 0x800B0100) // TRUST_E_NOSIGNATURE
                {
                    status = "NotSigned";
                }
                else
                {
                    status = "0x" + result.ToString("X8");
                }
            }

            // Extract Subject if it's Valid
            if (status == "Valid")
            {
                try
                {
                    // Microsoft official recommendation for SYSLIB0057:
                    // Use GetCertContentType() guard before calling the legacy constructor
                    if (X509Certificate2.GetCertContentType(filePath) == X509ContentType.Authenticode)
                    {
#pragma warning disable SYSLIB0057
                        using X509Certificate2 cert = new X509Certificate2(filePath);
#pragma warning restore SYSLIB0057
                        subject = cert.Subject;
                    }
                }
                catch
                {
                    // Subject extraction failed (expected for catalog-signed files)
                }
            }

            var responseObj = new
            {
                Status = status,
                Subject = subject
            };

            return JsonSerializer.Serialize(responseObj);
        }

        private static uint VerifyEmbedded(string filePath)
        {
            WINTRUST_FILE_INFO fileInfo = new WINTRUST_FILE_INFO();
            fileInfo.cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO));
            fileInfo.pcwszFilePath = filePath;
            fileInfo.hFile = IntPtr.Zero;
            fileInfo.pgKnownSubject = IntPtr.Zero;

            IntPtr pFileInfo = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
            Marshal.StructureToPtr(fileInfo, pFileInfo, false);

            WINTRUST_DATA wintrustData = new WINTRUST_DATA();
            wintrustData.cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA));
            wintrustData.dwUIChoice = WTD_UI_NONE;
            wintrustData.fdwRevocationChecks = WTD_REVOKE_NONE;
            wintrustData.dwUnionChoice = WTD_CHOICE_FILE;
            wintrustData.pInfo = pFileInfo;
            wintrustData.dwStateAction = WTD_STATEACTION_IGNORE;

            uint result = WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, ref wintrustData);
            Marshal.FreeHGlobal(pFileInfo);
            return result;
        }

        private static uint VerifyCatalog(string filePath)
        {
            IntPtr hCatAdmin = IntPtr.Zero;
            IntPtr hCatInfo = IntPtr.Zero;
            IntPtr pCatInfo = IntPtr.Zero;
            IntPtr pWintrustData = IntPtr.Zero;
            uint finalResult = 0x800B0100; // Default to NotSigned

            try
            {
                if (!CryptCATAdminAcquireContext(out hCatAdmin, IntPtr.Zero, 0))
                    return finalResult;

                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    IntPtr hFile = fs.SafeFileHandle.DangerousGetHandle();
                    uint cbHash = 0;
                    CryptCATAdminCalcHashFromFileHandle(hFile, ref cbHash, null, 0);
                    if (cbHash == 0) return finalResult;

                    byte[] pbHash = new byte[cbHash];
                    if (!CryptCATAdminCalcHashFromFileHandle(hFile, ref cbHash, pbHash, 0))
                        return finalResult;

                    string hashStr = BitConverter.ToString(pbHash).Replace("-", "").ToLower();

                    hCatInfo = CryptCATAdminEnumCatalogFromHash(hCatAdmin, pbHash, cbHash, 0, ref hCatInfo);
                    if (hCatInfo != IntPtr.Zero)
                    {
                        CATALOG_INFO catInfo = new CATALOG_INFO();
                        catInfo.cbStruct = (uint)Marshal.SizeOf(typeof(CATALOG_INFO));
                        if (CryptCATCatalogInfoFromContext(hCatInfo, ref catInfo, 0))
                        {
                            WINTRUST_CATALOG_INFO wtCatInfo = new WINTRUST_CATALOG_INFO();
                            wtCatInfo.cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_CATALOG_INFO));
                            wtCatInfo.pcwszCatalogFilePath = catInfo.wszCatalogFile;
                            wtCatInfo.pcwszMemberFilePath = filePath;
                            wtCatInfo.pcwszMemberTag = hashStr;

                            IntPtr pHash = Marshal.AllocHGlobal(pbHash.Length);
                            Marshal.Copy(pbHash, 0, pHash, pbHash.Length);
                            wtCatInfo.pbCalculatedFileHash = pHash;
                            wtCatInfo.cbCalculatedFileHash = (uint)pbHash.Length;

                            pCatInfo = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_CATALOG_INFO)));
                            Marshal.StructureToPtr(wtCatInfo, pCatInfo, false);

                            WINTRUST_DATA wtData = new WINTRUST_DATA();
                            wtData.cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA));
                            wtData.dwUIChoice = WTD_UI_NONE;
                            wtData.fdwRevocationChecks = WTD_REVOKE_NONE;
                            wtData.dwUnionChoice = WTD_CHOICE_CATALOG;
                            wtData.pInfo = pCatInfo;
                            wtData.dwStateAction = WTD_STATEACTION_VERIFY;

                            finalResult = WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, ref wtData);

                            // Close state
                            wtData.dwStateAction = WTD_STATEACTION_CLOSE;
                            WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, ref wtData);
                            Marshal.FreeHGlobal(pHash);
                        }
                        CryptCATAdminReleaseCatalogContext(hCatAdmin, hCatInfo, 0);
                    }
                }
            }
            catch
            {
                // Fallback to error
            }
            finally
            {
                if (hCatAdmin != IntPtr.Zero) CryptCATAdminReleaseContext(hCatAdmin, 0);
                if (pCatInfo != IntPtr.Zero) Marshal.FreeHGlobal(pCatInfo);
            }

            return finalResult;
        }
    }
}
