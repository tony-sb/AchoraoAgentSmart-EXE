#pragma once
#include <memory>
#include <string>
#include "Models.h"

class EraseStrategy {
public:
    virtual ~EraseStrategy() = default;
    virtual EraseMethod Method() const noexcept = 0;
    virtual SanitizeResult Execute(int deviceNumber,
                                   const std::string& serial,
                                   const std::string& model) = 0;
};

class HddOverwriteStrategy final : public EraseStrategy {
public:
    EraseMethod Method() const noexcept override { return EraseMethod::Overwrite; }
    SanitizeResult Execute(int deviceNumber,
                           const std::string& serial,
                           const std::string& model) override;
};

class AtaSecureEraseStrategy final : public EraseStrategy {
public:
    EraseMethod Method() const noexcept override { return EraseMethod::AtaSecureErase; }
    SanitizeResult Execute(int deviceNumber,
                           const std::string& serial,
                           const std::string& model) override;
};

class NvmeSanitizeStrategy final : public EraseStrategy {
public:
    EraseMethod Method() const noexcept override { return EraseMethod::NvmeSanitize; }
    SanitizeResult Execute(int deviceNumber,
                           const std::string& serial,
                           const std::string& model) override;
};

class EraseEngine {
public:
    explicit EraseEngine(int deviceNumber);

    StorageInfo DeviceInfo() const { return deviceInfo_; }
    EraseMethod SelectedMethod() const { return selectedMethod_; }

    SanitizeResult Run();
    void SetStrategy(std::unique_ptr<EraseStrategy> strategy);

private:
    std::unique_ptr<EraseStrategy> ResolveStrategy() const;
    std::unique_ptr<EraseStrategy> strategy_;
    StorageInfo deviceInfo_;
    EraseMethod selectedMethod_{EraseMethod::None};
    int deviceNumber_{0};
};
