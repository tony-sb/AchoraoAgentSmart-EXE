#pragma once
#include <string>
#include <memory>
#include <mutex>
#include <fstream>

enum class LogLevel {
    Debug,
    Info,
    Warning,
    Error
};

class Logger {
public:
    static Logger& Instance();

    void Open(const std::string& filePath = "");
    void Close();

    void Log(LogLevel level, const std::string& module, const std::string& message);
    void Debug(const std::string& module, const std::string& msg) { Log(LogLevel::Debug, module, msg); }
    void Info(const std::string& module, const std::string& msg)   { Log(LogLevel::Info, module, msg); }
    void Warn(const std::string& module, const std::string& msg)   { Log(LogLevel::Warning, module, msg); }
    void Error(const std::string& module, const std::string& msg)  { Log(LogLevel::Error, module, msg); }

    std::string Timestamp() const;

    Logger(const Logger&) = delete;
    Logger& operator=(const Logger&) = delete;

private:
    Logger() = default;
    ~Logger();
    std::string LevelPrefix(LogLevel level) const;

    std::ofstream file_;
    std::mutex mtx_;
    bool console_{true};
};
