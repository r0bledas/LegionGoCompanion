using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace HandheldCompanion.ADLX
{
    public static class ADLXBackend
    {
        public const string ADLX_Wrapper = @".\Resources\AMD\ADLX_Wrapper.dll";

        public enum ADLX_RESULT
        {
            ADLX_OK = 0,                    /**< @ENG_START_DOX This result indicates success. @ENG_END_DOX */
            ADLX_ALREADY_ENABLED,           /**< @ENG_START_DOX This result indicates that the asked action is already enabled. @ENG_END_DOX */
            ADLX_ALREADY_INITIALIZED,       /**< @ENG_START_DOX This result indicates that ADLX has a unspecified type of initialization. @ENG_END_DOX */
            ADLX_FAIL,                      /**< @ENG_START_DOX This result indicates an unspecified failure. @ENG_END_DOX */
            ADLX_INVALID_ARGS,              /**< @ENG_START_DOX This result indicates that the arguments are invalid. @ENG_END_DOX */
            ADLX_BAD_VER,                   /**< @ENG_START_DOX This result indicates that the asked version is incompatible with the current version. @ENG_END_DOX */
            ADLX_UNKNOWN_INTERFACE,         /**< @ENG_START_DOX This result indicates that an unknown interface was asked. @ENG_END_DOX */
            ADLX_TERMINATED,                /**< @ENG_START_DOX This result indicates that the calls were made in an interface after ADLX was terminated. @ENG_END_DOX */
            ADLX_ADL_INIT_ERROR,            /**< @ENG_START_DOX This result indicates that the ADL initialization failed. @ENG_END_DOX */
            ADLX_NOT_FOUND,                 /**< @ENG_START_DOX This result indicates that the item is not found. @ENG_END_DOX */
            ADLX_INVALID_OBJECT,            /**< @ENG_START_DOX This result indicates that the method was called into an invalid object. @ENG_END_DOX */
            ADLX_ORPHAN_OBJECTS,            /**< @ENG_START_DOX This result indicates that ADLX was terminated with outstanding ADLX objects. Any interface obtained from ADLX points to invalid memory and calls in their methods will result in unexpected behavior. @ENG_END_DOX */
            ADLX_NOT_SUPPORTED,             /**< @ENG_START_DOX This result indicates that the asked feature is not supported. @ENG_END_DOX */
            ADLX_PENDING_OPERATION,         /**< @ENG_START_DOX This result indicates a failure due to an operation currently in progress. @ENG_END_DOX */
            ADLX_GPU_INACTIVE               /**< @ENG_START_DOX This result indicates that the GPU is inactive. @ENG_END_DOX */
        }

        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] public static extern bool IntializeAdlx(StringBuilder dispName, int nameLength);
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool InitializeAdlxWithIncompatibleDriver();
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool CloseAdlx();

        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern ADLX_RESULT GetNumberOfDisplays(ref int displayNum);

        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] public static extern ADLX_RESULT GetDisplayName(int idx, StringBuilder dispName, int nameLength);

        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern ADLX_RESULT GetDisplayGPU(int idx, ref int UniqueId);

        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern ADLX_RESULT GetGPUIndex(int UniqueId, ref int idx);

        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool HasRSRSupport();
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool GetRSR();
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool SetRSR(bool enable);
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern int GetRSRSharpness();
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool SetRSRSharpness(int sharpness);

        public enum ADLX_AFMF_ALGORITHM
        {
            AFMF_ALGORITHM_AUTO = 0, /**< @ENG_START_DOX The algorithm used by AMD Fluid Motion Frames is auto. @ENG_END_DOX */
            AFMF_ALGORITHM_ENHANCED = 1, /**< @ENG_START_DOX The algorithm used by AMD Fluid Motion Frames is enhanced mode. @ENG_END_DOX */
            AFMF_ALGORITHM_STANDARD = 2, /**< @ENG_START_DOX The algorithm used by AMD Fluid Motion Frames is standard mode. @ENG_END_DOX */
        }

        public enum ADLX_AFMF_SEARCH_MODE_TYPE
        {
            AFMF_SEARCH_MODE_AUTO = 0, /**< @ENG_START_DOX The AMD Fluid Motion Frames search mode is auto. @ENG_END_DOX */
            AFMF_SEARCH_MODE_STANDARD = 1, /**< @ENG_START_DOX The AMD Fluid Motion Frames search mode is standard. @ENG_END_DOX */
            AFMF_SEARCH_MODE_HIGH = 2, /**< @ENG_START_DOX The AMD Fluid Motion Frames search mode is high. @ENG_END_DOX */
        }

        public enum ADLX_AFMF_PERFORMANCE_MODE_TYPE
        {
            AFMF_PERFORMANCE_MODE_AUTO = 0, /**< @ENG_START_DOX The AMD Fluid Motion Frames performance mode is auto. @ENG_END_DOX */
            AFMF_PERFORMANCE_MODE_QUALITY = 1, /**< @ENG_START_DOX The AMD Fluid Motion Frames performance mode is quality. @ENG_END_DOX */
            AFMF_PERFORMANCE_MODE_PERFORMANCE = 2, /**< @ENG_START_DOX The AMD Fluid Motion Frames performance mode is performance. @ENG_END_DOX */
        }

        public enum ADLX_AFMF_FAST_MOTION_RESP
        {
            AFMF_RESP_REPEAT_FRAMES = 0, /**< @ENG_START_DOX The AMD Fluid Motion Frames approach to fast-motion content is to repeat the frames. @ENG_END_DOX */
            AFMF_RESP_BLENDED_FRAMES = 1, /**< @ENG_START_DOX The AMD Fluid Motion Frames approach to fast-motion content is to blend the frames. @ENG_END_DOX */
        }

        public enum ADLX_FFX_FRAME_GEN_RATIO
        {
            FFX_FRAME_GEN_UNKNOWN = 0, /**< @ENG_START_DOX The frame generation ratio is unknown. @ENG_END_DOX */
            FFX_FRAME_GEN_2X = 1, /**< @ENG_START_DOX The frame generation ratio is 2X. @ENG_END_DOX */
        }

        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool HasAFMFSupport();
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool GetAFMF();
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool SetAFMF(bool enable);

        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool GetAFMFAlgorithmSupport();
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern int GetAFMFAlgorithm();
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool SetAFMFAlgorithm(int algorithm);
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern int GetAFMFSearchMode();
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool SetAFMFSearchMode(int mode);
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern int GetAFMFPerformanceMode();
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool SetAFMFPerformanceMode(int mode);
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern int GetAFMFFastMotionResponse();
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool SetAFMFFastMotionResponse(int response);

        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool GetAntiLag(int GPU);
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool SetAntiLag(int GPU, bool enable);

        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool GetBoost(int GPU);
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool SetBoost(int GPU, bool enable);
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern int GetBoostResolution(int GPU);
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool SetBoostResolution(int GPU, int minRes);

        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool GetChill(int GPU);
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool SetChill(int GPU, bool enable);
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern int GetChillMinFPS(int GPU);
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool SetChillMinFPS(int GPU, int minFPS);
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern int GetChillMaxFPS(int GPU);
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool SetChillMaxFPS(int GPU, int maxFPS);

        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool GetImageSharpening(int GPU);
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool SetImageSharpening(int GPU, bool enable);
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern int GetImageSharpeningSharpness(int GPU);
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool SetImageSharpeningSharpness(int GPU, int sharpness);

        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool HasSharpenDesktopSupport(int GPU);
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool GetSharpenDesktop(int GPU);
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool SetSharpenDesktop(int GPU, bool enable);

        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool HasFSRSupport(int GPU);
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool GetFSR(int GPU);
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool SetFSR(int GPU, bool enable);

        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool HasFFXFrameGenSupport(int GPU);
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool GetFFXFrameGen(int GPU);
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool SetFFXFrameGen(int GPU, bool enable);
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern int GetFFXFrameGenRatio(int GPU);
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool SetFFXFrameGenRatio(int GPU, int ratio);

        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool HasIntegerScalingSupport(int displayIdx);
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool GetIntegerScaling(int displayIdx);
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool SetIntegerScaling(int displayIdx, bool enabled);

        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool HasGPUScalingSupport(int displayIdx);
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool GetGPUScaling(int displayIdx);
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool SetGPUScaling(int displayIdx, bool enabled);

        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool HasScalingModeSupport(int displayIdx);
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern int GetScalingMode(int displayIdx);
        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool SetScalingMode(int displayIdx, int mode);

        [DllImport(ADLX_Wrapper, CallingConvention = CallingConvention.Cdecl)] public static extern bool GetAdlxTelemetry(int GPU, ref AdlxTelemetryData adlxTelemetryData);

        public static double GetGPUTemperature(int GPU)
        {
            try
            {
                if (!IsInitialized) return 0;
                var data = new AdlxTelemetryData();
                if (GetAdlxTelemetry(GPU, ref data))
                {
                    if (data.gpuTemperatureSupported && data.gpuTemperatureValue > 0)
                        return data.gpuTemperatureValue;
                    if (data.gpuHotspotTemperatureSupported && data.gpuHotspotTemperatureValue > 0)
                        return data.gpuHotspotTemperatureValue;
                }
            }
            catch { }
            return 0;
        }

        public static int GetGPUFanSpeed(int GPU)
        {
            try
            {
                if (!IsInitialized) return 0;
                var data = new AdlxTelemetryData();
                if (GetAdlxTelemetry(GPU, ref data) && data.gpuFanSpeedSupported && data.gpuFanSpeedValue > 0)
                    return (int)System.Math.Round(data.gpuFanSpeedValue);
            }
            catch { }
            return 0;
        }

        #region AMD ADL2 (atiadlxx.dll) Direct Driver Bindings for AFMF & Vivid Colors
        private const string ATI_ADL_DLL = "atiadlxx.dll";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate System.IntPtr ADL_Main_Memory_Alloc_Delegate(int size);

        private static readonly ADL_Main_Memory_Alloc_Delegate AdlAllocCallback = size => Marshal.AllocHGlobal(size);

        [DllImport(ATI_ADL_DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL2_Main_Control_Create(ADL_Main_Memory_Alloc_Delegate callback, int iEnumConnectedAdapters, out System.IntPtr context);

        [DllImport(ATI_ADL_DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL2_Main_Control_Destroy(System.IntPtr context);

        [DllImport(ATI_ADL_DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL2_DriverFrameGenerationSupport_Get(System.IntPtr context, int iAdapterIndex, out int lpSupported);

        [DllImport(ATI_ADL_DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL2_DriverFrameGeneration_Settings_Get(System.IntPtr context, int iAdapterIndex, out int lpEnabled);

        [DllImport(ATI_ADL_DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL2_DriverFrameGeneration_Settings_Set(System.IntPtr context, int iAdapterIndex, int iEnabled);

        [DllImport(ATI_ADL_DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL2_Display_SCE_Supported(System.IntPtr context, int iAdapterIndex, int iDisplayIndex, out int lpSupported);

        [DllImport(ATI_ADL_DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL2_Display_SCE_State_Get(System.IntPtr context, int iAdapterIndex, int iDisplayIndex, out int lpState, out int lpDefState);

        [DllImport(ATI_ADL_DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL2_Display_SCE_State_Set(System.IntPtr context, int iAdapterIndex, int iDisplayIndex, int iState);

        // ADL_COLOR_BRIGHTNESS = 1, ADL_COLOR_CONTRAST = 2, ADL_COLOR_HUE = 3, ADL_COLOR_SATURATION = 4, ADL_COLOR_TEMPERATURE = 5
        [DllImport(ATI_ADL_DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL2_Display_Color_Get(System.IntPtr context, int iAdapterIndex, int iDisplayIndex, int iColorType, out int lpCur, out int lpDef, out int lpMin, out int lpMax, out int lpStep);

        [DllImport(ATI_ADL_DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL2_Display_Color_Set(System.IntPtr context, int iAdapterIndex, int iDisplayIndex, int iColorType, int iValue);

        public static bool HasAdl2AFMF()
        {
            System.IntPtr ctx = System.IntPtr.Zero;
            try
            {
                if (ADL2_Main_Control_Create(AdlAllocCallback, 1, out ctx) == 0 && ctx != System.IntPtr.Zero)
                {
                    if (ADL2_DriverFrameGenerationSupport_Get(ctx, 0, out int sup) == 0 && (sup & 0xFF) != 0)
                        return true;
                }
            }
            catch { }
            finally
            {
                if (ctx != System.IntPtr.Zero)
                {
                    try { ADL2_Main_Control_Destroy(ctx); } catch { }
                }
            }
            try { return HasAFMFSupport(); } catch { return false; }
        }

        public static bool GetAdl2AFMF()
        {
            System.IntPtr ctx = System.IntPtr.Zero;
            try
            {
                if (ADL2_Main_Control_Create(AdlAllocCallback, 1, out ctx) == 0 && ctx != System.IntPtr.Zero)
                {
                    if (ADL2_DriverFrameGeneration_Settings_Get(ctx, 0, out int en) == 0)
                        return en != 0;
                }
            }
            catch { }
            finally
            {
                if (ctx != System.IntPtr.Zero)
                {
                    try { ADL2_Main_Control_Destroy(ctx); } catch { }
                }
            }
            try { return GetAFMF(); } catch { return false; }
        }

        public static bool SetAdl2AFMF(bool enable)
        {
            System.IntPtr ctx = System.IntPtr.Zero;
            try
            {
                if (ADL2_Main_Control_Create(AdlAllocCallback, 1, out ctx) == 0 && ctx != System.IntPtr.Zero)
                {
                    if (ADL2_DriverFrameGeneration_Settings_Set(ctx, 0, enable ? 1 : 0) == 0)
                        return true;
                }
            }
            catch { }
            finally
            {
                if (ctx != System.IntPtr.Zero)
                {
                    try { ADL2_Main_Control_Destroy(ctx); } catch { }
                }
            }
            try { return SetAFMF(enable); } catch { return false; }
        }

        public static bool HasVividColorSupport(int displayIdx = 0)
        {
            System.IntPtr ctx = System.IntPtr.Zero;
            try
            {
                if (ADL2_Main_Control_Create(AdlAllocCallback, 1, out ctx) == 0 && ctx != System.IntPtr.Zero)
                {
                    if (ADL2_Display_SCE_Supported(ctx, 0, displayIdx, out int sup) == 0 && sup != 0)
                        return true;
                    if (ADL2_Display_Color_Get(ctx, 0, displayIdx, 4, out _, out _, out _, out _, out _) == 0)
                        return true;
                }
            }
            catch { }
            finally
            {
                if (ctx != System.IntPtr.Zero)
                {
                    try { ADL2_Main_Control_Destroy(ctx); } catch { }
                }
            }
            return false;
        }

        public static bool GetVividColors(int displayIdx = 0)
        {
            System.IntPtr ctx = System.IntPtr.Zero;
            try
            {
                if (ADL2_Main_Control_Create(AdlAllocCallback, 1, out ctx) == 0 && ctx != System.IntPtr.Zero)
                {
                    if (ADL2_Display_SCE_State_Get(ctx, 0, displayIdx, out int st, out _) == 0 && st == 2)
                        return true;
                    if (ADL2_Display_Color_Get(ctx, 0, displayIdx, 4, out int sat, out int defSat, out _, out _, out _) == 0 && sat > defSat + 5)
                        return true;
                }
            }
            catch { }
            finally
            {
                if (ctx != System.IntPtr.Zero)
                {
                    try { ADL2_Main_Control_Destroy(ctx); } catch { }
                }
            }
            return false;
        }

        public static bool SetVividColors(int displayIdx, bool enable)
        {
            System.IntPtr ctx = System.IntPtr.Zero;
            try
            {
                if (ADL2_Main_Control_Create(AdlAllocCallback, 1, out ctx) == 0 && ctx != System.IntPtr.Zero)
                {
                    // Set AMD Vivid Gaming hardware profile (SCE state 2 = Vivid Gaming, 1 = Default)
                    int rSce = ADL2_Display_SCE_State_Set(ctx, 0, displayIdx, enable ? 2 : 1);
                    // Also set Display Color Saturation (type 4: 135 when Vivid On, 100 when Off) & Contrast (type 2: 110 when Vivid On, 100 when Off)
                    int rSat = ADL2_Display_Color_Set(ctx, 0, displayIdx, 4, enable ? 135 : 100);
                    int rCon = ADL2_Display_Color_Set(ctx, 0, displayIdx, 2, enable ? 110 : 100);
                    return rSce == 0 || rSat == 0 || rCon == 0;
                }
            }
            catch { }
            finally
            {
                if (ctx != System.IntPtr.Zero)
                {
                    try { ADL2_Main_Control_Destroy(ctx); } catch { }
                }
            }
            return false;
        }
        #endregion

        private static StringBuilder displayName = new StringBuilder(256);
        public static volatile bool IsInitialized = false;

        static ADLXBackend() { }

        [SecurityCritical]
        public static bool SafeIntializeAdlx()
        {
            try
            {
                // Try the full ADLX initialisation first; ADLX_Wrapper.dll handles
                // its own internal driver compatibility check.  Only fall back to
                // the incompatible-driver path if the wrapper explicitly returns false.
                if (IntializeAdlx(displayName, displayName.Capacity))
                {
                    IsInitialized = true;
                    return true;
                }

                bool fallbackOk = InitializeAdlxWithIncompatibleDriver();
                if (fallbackOk)
                    IsInitialized = true;
                return fallbackOk;
            }
            catch { }

            return false;
        }

        public static string GetVersion()
        {
            return displayName.ToString();
        }
    }
}
