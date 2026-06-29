#pragma once

#if defined(_WIN32) || defined(_WIN64)
#  if defined(AchoraoEngine_EXPORTS)
#    define ENGINE_API __declspec(dllexport)
#  else
#    define ENGINE_API __declspec(dllimport)
#  endif
#else
#  define ENGINE_API __attribute__((visibility("default")))
#endif

extern "C" {

ENGINE_API bool Engine_Initialize(const char* logPath);
ENGINE_API void Engine_Shutdown();

ENGINE_API const char* Engine_EnumerateStorage();
ENGINE_API const char* Engine_ClassifyDevice(int deviceNumber);
ENGINE_API const char* Engine_ReadSmart(int deviceNumber);

ENGINE_API const char* Engine_DetectMemory();
ENGINE_API const char* Engine_DetectGPU();

ENGINE_API const char* Engine_ExecuteSanitize(int deviceNumber);
ENGINE_API const char* Engine_VerifySanitization(int deviceNumber);

ENGINE_API const char* Engine_BuildEvidence(int deviceNumber,
                                             const char* technicianId,
                                             const char* workstation);

ENGINE_API void Engine_FreeString(const char* str);
ENGINE_API bool Engine_IsSystemDisk(int deviceNumber);

}
