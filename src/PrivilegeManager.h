#pragma once

class PrivilegeManager {
public:
    static bool IsElevated();
    static bool IsSystemDisk(int deviceNumber);
    static void AssertSafeTarget(int deviceNumber);
};
