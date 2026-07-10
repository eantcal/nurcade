// This file is part of the WinRayCast Application (a 3D Engine Demo).
// Copyright (C) 2005 - 2018
// Antonino Calderone (antonino.calderone@gmail.com)
// All rights reserved.
// Licensed under the MIT License.
// See COPYING file in the project root for full license information.

#include "TextToSpeechPlayer.h"

#include <windows.h>
#include <mmsystem.h>
#include <sapi.h>
#include <wrl/client.h>

#ifdef WINRAYCAST_HAS_SHERPA_ONNX_TTS
#include <sherpa-onnx/c-api/c-api.h>
#endif

#include <sstream>

namespace {
using Microsoft::WRL::ComPtr;

std::string hresultText(HRESULT result)
{
    char text[256] = { 0 };
    const auto length = FormatMessageA(
        FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
        nullptr,
        static_cast<DWORD>(result),
        MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT),
        text,
        sizeof(text),
        nullptr);

    if (length > 0) {
        return text;
    }

    std::ostringstream stream;
    stream << "HRESULT 0x" << std::hex << static_cast<unsigned long>(result);
    return stream.str();
}

std::wstring utf8ToWide(const std::string& text)
{
    if (text.empty()) {
        return {};
    }

    auto needed = MultiByteToWideChar(
        CP_UTF8,
        MB_ERR_INVALID_CHARS,
        text.c_str(),
        -1,
        nullptr,
        0);

    auto codePage = CP_UTF8;
    auto flags = MB_ERR_INVALID_CHARS;

    if (needed == 0) {
        needed = MultiByteToWideChar(
            CP_ACP,
            0,
            text.c_str(),
            -1,
            nullptr,
            0);
        codePage = CP_ACP;
        flags = 0;
    }

    if (needed <= 0) {
        return {};
    }

    std::wstring result(static_cast<size_t>(needed), L'\0');
	const auto written = MultiByteToWideChar(
		codePage,
		flags,
		text.c_str(),
		-1,
		&result[0],
		needed);

    if (written == 0) {
        return {};
    }

    if (!result.empty() && result.back() == L'\0') {
        result.pop_back();
    }

    return result;
}

std::string wideToUtf8(const std::wstring& text)
{
    if (text.empty()) {
        return {};
    }

    const auto needed = WideCharToMultiByte(
        CP_UTF8, 0, text.c_str(), -1, nullptr, 0, nullptr, nullptr);
    if (needed <= 0) {
        return {};
    }

    std::string result(static_cast<size_t>(needed), '\0');
    if (WideCharToMultiByte(
        CP_UTF8, 0, text.c_str(), -1, result.data(), needed, nullptr, nullptr) == 0) {
        return {};
    }
    if (!result.empty() && result.back() == '\0') {
        result.pop_back();
    }
    return result;
}

std::string executableDirectory()
{
    std::wstring path(MAX_PATH, L'\0');
    const auto written = GetModuleFileNameW(nullptr, path.data(), MAX_PATH);
    if (written == 0 || written >= MAX_PATH) {
        return {};
    }
    path.resize(written);
    const auto slash = path.find_last_of(L"/\\");
    if (slash == std::wstring::npos) {
        return {};
    }
    return wideToUtf8(path.substr(0, slash + 1));
}

std::string environmentValue(const char* name)
{
    const auto needed = GetEnvironmentVariableA(name, nullptr, 0);
    if (needed == 0) {
        return {};
    }

    std::string value(static_cast<size_t>(needed), '\0');
    const auto written = GetEnvironmentVariableA(name, value.data(), needed);
    if (written == 0 || written >= needed) {
        return {};
    }
    value.resize(written);
    return value;
}

bool fileExists(const std::string& path) noexcept
{
    return !path.empty()
        && GetFileAttributesA(path.c_str()) != INVALID_FILE_ATTRIBUTES;
}
}

struct TextToSpeechPlayer::Impl {
    ComPtr<ISpVoice> voice;
    bool comInitialized = false;
    std::string backend = "AUTO";
    std::string diagnostic;
#ifdef WINRAYCAST_HAS_SHERPA_ONNX_TTS
    const SherpaOnnxOfflineTts* neuralVoice = nullptr;
    bool neuralChecked = false;
    std::wstring neuralWavePath;
#endif
};

TextToSpeechPlayer::TextToSpeechPlayer()
    : m_impl(std::make_unique<Impl>())
{
}

TextToSpeechPlayer::~TextToSpeechPlayer()
{
    stop();
#ifdef WINRAYCAST_HAS_SHERPA_ONNX_TTS
    if (m_impl->neuralVoice != nullptr) {
        SherpaOnnxDestroyOfflineTts(m_impl->neuralVoice);
        m_impl->neuralVoice = nullptr;
    }
#endif
    m_impl->voice.Reset();

    if (m_impl->comInitialized) {
        CoUninitialize();
    }
}

bool TextToSpeechPlayer::ensureNeuralReady()
{
#ifndef WINRAYCAST_HAS_SHERPA_ONNX_TTS
    m_impl->diagnostic = "sherpa-onnx was not enabled at build time.";
    return false;
#else
    if (m_impl->neuralChecked) {
        return m_impl->neuralVoice != nullptr;
    }

    m_impl->neuralChecked = true;
    const auto defaultModelDirectory = executableDirectory() + "tts-model\\";
    auto model = environmentValue("WINRAYCAST_TTS_MODEL");
    auto tokens = environmentValue("WINRAYCAST_TTS_TOKENS");
    auto dataDir = environmentValue("WINRAYCAST_TTS_DATA_DIR");
    const auto lexicon = environmentValue("WINRAYCAST_TTS_LEXICON");
    if (model.empty()) {
        model = defaultModelDirectory + "en_GB-alan-medium.onnx";
    }
    if (tokens.empty()) {
        tokens = defaultModelDirectory + "tokens.txt";
    }
    if (dataDir.empty() && lexicon.empty()) {
        dataDir = defaultModelDirectory + "espeak-ng-data";
    }
    if (!fileExists(model) || !fileExists(tokens)
        || (!fileExists(dataDir) && !fileExists(lexicon))) {
        m_impl->diagnostic = "sherpa-onnx TTS assets were not found. Expected model '"
            + model + "', tokens '" + tokens + "', and espeak data or lexicon under '"
            + defaultModelDirectory + "'.";
        return false;
    }

    SherpaOnnxOfflineTtsConfig config{};
    config.model.vits.model = model.c_str();
    config.model.vits.tokens = tokens.c_str();
    config.model.vits.data_dir = dataDir.empty() ? nullptr : dataDir.c_str();
    config.model.vits.lexicon = lexicon.empty() ? nullptr : lexicon.c_str();
    config.model.vits.noise_scale = 0.667f;
    config.model.vits.noise_scale_w = 0.8f;
    config.model.vits.length_scale = 1.0f;
    config.model.num_threads = 2;
    config.model.provider = "cpu";
    config.max_num_sentences = 1;
    m_impl->neuralVoice = SherpaOnnxCreateOfflineTts(&config);
    if (m_impl->neuralVoice == nullptr) {
        m_impl->diagnostic = "sherpa-onnx could not create the offline TTS engine.";
        return false;
    }

    wchar_t tempDirectory[MAX_PATH]{};
    if (GetTempPathW(MAX_PATH, tempDirectory) == 0) {
        SherpaOnnxDestroyOfflineTts(m_impl->neuralVoice);
        m_impl->neuralVoice = nullptr;
        m_impl->diagnostic = "Windows did not provide a temporary directory for neural TTS audio.";
        return false;
    }
    m_impl->neuralWavePath = tempDirectory;
    m_impl->neuralWavePath += L"WinRayCast-neural-tts.wav";
    return true;
#endif
}

bool TextToSpeechPlayer::speakNeural(const std::string& text)
{
#ifndef WINRAYCAST_HAS_SHERPA_ONNX_TTS
    (void)text;
    return false;
#else
    if (!ensureNeuralReady()) {
        return false;
    }

    SherpaOnnxGenerationConfig config{};
    config.silence_scale = 0.2f;
    config.speed = 1.0f;
    const auto* audio = SherpaOnnxOfflineTtsGenerateWithConfig(
        m_impl->neuralVoice,
        text.c_str(),
        &config,
        nullptr,
        nullptr);
    if (audio == nullptr || audio->samples == nullptr || audio->n <= 0) {
        if (audio != nullptr) {
            SherpaOnnxDestroyOfflineTtsGeneratedAudio(audio);
        }
        m_impl->diagnostic = "sherpa-onnx generated no audio samples.";
        return false;
    }

    const auto pathUtf8 = wideToUtf8(m_impl->neuralWavePath);
    const auto written = SherpaOnnxWriteWave(
        audio->samples,
        audio->n,
        audio->sample_rate,
        pathUtf8.c_str());
    SherpaOnnxDestroyOfflineTtsGeneratedAudio(audio);
    if (written == 0) {
        m_impl->diagnostic = "sherpa-onnx could not write the generated wave file.";
        return false;
    }

    if (PlaySoundW(
        m_impl->neuralWavePath.c_str(),
        nullptr,
        SND_ASYNC | SND_FILENAME | SND_NODEFAULT) == FALSE) {
        m_impl->diagnostic = "Windows could not play the generated sherpa-onnx wave file.";
        return false;
    }

    m_impl->diagnostic.clear();
    return true;
#endif
}

bool TextToSpeechPlayer::ensureReady(std::string* error)
{
    if (m_impl->voice) {
        return true;
    }

    const auto initResult = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (SUCCEEDED(initResult)) {
        m_impl->comInitialized = true;
    }
    else if (initResult != RPC_E_CHANGED_MODE) {
        if (error != nullptr) {
            *error = "Could not initialize COM for text-to-speech: "
                + hresultText(initResult);
        }
        return false;
    }

    const auto createResult = CoCreateInstance(
        CLSID_SpVoice,
        nullptr,
        CLSCTX_ALL,
        IID_PPV_ARGS(&m_impl->voice));

    if (FAILED(createResult) || !m_impl->voice) {
        if (error != nullptr) {
            *error = "Could not create the Windows text-to-speech voice: "
                + hresultText(createResult);
        }
        return false;
    }

    return true;
}

bool TextToSpeechPlayer::speak(const std::string& text, std::string* error)
{
    if (text.empty()) {
        return true;
    }

    if (speakNeural(text)) {
        m_impl->backend = "SHERPA";
        OutputDebugStringA("WinRayCast TTS backend: sherpa-onnx\n");
        return true;
    }

    if (!m_impl->diagnostic.empty()) {
        OutputDebugStringA(("WinRayCast TTS sherpa-onnx fallback: "
            + m_impl->diagnostic + "\n").c_str());
    }

    const auto wideText = utf8ToWide(text);
    if (wideText.empty()) {
        return true;
    }

    if (!ensureReady(error)) {
        return false;
    }

    const auto speakResult = m_impl->voice->Speak(
        wideText.c_str(),
        SPF_ASYNC | SPF_PURGEBEFORESPEAK | SPF_IS_NOT_XML,
        nullptr);

    if (FAILED(speakResult)) {
        if (error != nullptr) {
            *error = "Could not speak event message: " + hresultText(speakResult);
        }
        return false;
    }

    m_impl->backend = "SAPI";
    OutputDebugStringA("WinRayCast TTS backend: Windows SAPI\n");
    return true;
}

const std::string& TextToSpeechPlayer::backendName() const noexcept
{
    return m_impl->backend;
}

const std::string& TextToSpeechPlayer::diagnosticMessage() const noexcept
{
    return m_impl->diagnostic;
}

void TextToSpeechPlayer::stop() noexcept
{
#ifdef WINRAYCAST_HAS_SHERPA_ONNX_TTS
    PlaySoundW(nullptr, nullptr, 0);
#endif
    if (!m_impl || !m_impl->voice) {
        return;
    }

    m_impl->voice->Speak(L"", SPF_ASYNC | SPF_PURGEBEFORESPEAK, nullptr);
}
