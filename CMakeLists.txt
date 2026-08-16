cmake_minimum_required(VERSION 3.20)

project(nuRCADE
    VERSION 1.2.0
    DESCRIPTION "New (nu) Raycasting Classic Arcade Development Engine"
    LANGUAGES CXX RC)

if(NOT WIN32)
    message(FATAL_ERROR "nuRCADE currently targets Windows only.")
endif()

include(CTest)
include(FetchContent)

option(NURCADE_ENABLE_NEURAL_TTS
    "Use sherpa-onnx neural TTS when the library and a runtime model are available"
    ON)
option(NURCADE_FETCH_NEURAL_TTS
    "Download the official sherpa-onnx Windows SDK and default Piper voice when needed"
    ON)

if(MSVC AND MSVC_VERSION GREATER_EQUAL 1950)
    set(CMAKE_CXX_COMPILE_FEATURES
        cxx_std_98
        cxx_std_11
        cxx_range_for
        cxx_std_14
        cxx_std_17
        cxx_std_20
        cxx_std_23)
    add_compile_options("$<$<COMPILE_LANGUAGE:CXX>:/std:c++20>")
else()
    set(CMAKE_CXX_STANDARD 20)
    set(CMAKE_CXX_STANDARD_REQUIRED ON)
    set(CMAKE_CXX_EXTENSIONS OFF)
endif()

set(JSON_Install OFF CACHE BOOL "" FORCE)
FetchContent_Declare(
    nlohmann_json
    URL https://github.com/nlohmann/json/releases/download/v3.11.3/json.tar.xz
    DOWNLOAD_EXTRACT_TIMESTAMP TRUE)
FetchContent_MakeAvailable(nlohmann_json)

set(NURCADE_ENGINE_SOURCES
    src/engine/ActorSystem.cpp
    src/engine/Player.cpp
    src/engine/RayCaster.cpp
    src/engine/RaycastEngine.cpp
    src/engine/SceneLoader.cpp
    src/engine/SpriteManager.cpp
    src/engine/SpriteMetadataLoader.cpp
    src/engine/SpriteSet.cpp
    src/engine/ViewWeapon.cpp
    src/engine/WorldJsonLoader.cpp
    src/engine/WorldMap.cpp)

set(NURCADE_ENGINE_HEADERS
    src/engine/ActorSystem.h
    src/engine/Color.h
    src/engine/ColumnDepthBuffer.h
    src/engine/FrameBuffer.h
    src/engine/Player.h
    src/engine/MapCell.h
    src/engine/RayCaster.h
    src/engine/RaycastEngine.h
    src/engine/SceneLoader.h
    src/engine/Sprite.h
    src/engine/SpriteManager.h
    src/engine/SpriteMetadataLoader.h
    src/engine/SpriteSet.h
    src/engine/SpriteProjection.h
    src/engine/Texture.h
    src/engine/ViewWeapon.h
    src/engine/WorldBlock.h
    src/engine/WorldJsonLoader.h
    src/engine/WorldMap.h)

set(NURCADE_WIN_SUPPORT_SOURCES
    src/win/BackgroundMusicPlayer.cpp
    src/win/D2dPresenter.cpp
    src/win/DdxDevice.cpp
    src/win/OggVorbisDecoder.cpp
    src/win/SoundEffectPlayer.cpp
    src/win/TextToSpeechPlayer.cpp
    src/win/WinFramePresenter.cpp
    src/win/WinTextureLoader.cpp)

set(NURCADE_WIN_SUPPORT_HEADERS
    src/win/BackgroundMusicPlayer.h
    src/win/D2dPresenter.h
    src/win/DdxDevice.h
    src/win/OggVorbisDecoder.h
    src/win/SoundEffectPlayer.h
    src/win/TextToSpeechPlayer.h
    src/win/WinFramePresenter.h
    src/win/WinTextureLoader.h)

set(NURCADE_DEMO_SOURCES
    src/app/nuRCADE.cpp
    nuRCADE.rc)

set(NURCADE_DEMO_HEADERS
    src/app/3ddemoconfig.h
    src/app/nuRCADE.h
    nuRCADE-resource.h)

file(GLOB_RECURSE NURCADE_RESOURCE_FILES CONFIGURE_DEPENDS
    "${CMAKE_CURRENT_SOURCE_DIR}/res/worlds/*"
    "${CMAKE_CURRENT_SOURCE_DIR}/res/examples/*")

add_library(nuRCADEEngine STATIC
    ${NURCADE_ENGINE_SOURCES}
    ${NURCADE_ENGINE_HEADERS})

target_include_directories(nuRCADEEngine PUBLIC
    "${CMAKE_CURRENT_SOURCE_DIR}/src/engine")

target_compile_definitions(nuRCADEEngine PUBLIC
    WIN32
    _MBCS
    $<$<CONFIG:Debug>:_DEBUG>
    $<$<NOT:$<CONFIG:Debug>>:NDEBUG>)

target_link_libraries(nuRCADEEngine PUBLIC
    nlohmann_json::nlohmann_json)

add_library(nuRCADEWinSupport STATIC
    ${NURCADE_WIN_SUPPORT_SOURCES}
    ${NURCADE_WIN_SUPPORT_HEADERS})

set(NURCADE_SHERPA_ONNX_TARGET "")
set(NURCADE_SHERPA_ONNX_RUNTIME_DIR "")
set(NURCADE_SHERPA_ONNX_RUNTIME_DIRS "")
set(NURCADE_TTS_MODEL_DIR "")
if(NURCADE_ENABLE_NEURAL_TTS)
    find_package(PkgConfig QUIET)
    if(PkgConfig_FOUND)
        pkg_check_modules(SHERPA_ONNX QUIET IMPORTED_TARGET sherpa-onnx)
        if(TARGET PkgConfig::SHERPA_ONNX)
            set(NURCADE_SHERPA_ONNX_TARGET PkgConfig::SHERPA_ONNX)
        endif()
    endif()

    if(NOT NURCADE_SHERPA_ONNX_TARGET)
        find_path(SHERPA_ONNX_INCLUDE_DIR
            NAMES sherpa-onnx/c-api/c-api.h
            HINTS ENV SHERPA_ONNX_ROOT
            PATH_SUFFIXES include)
        find_library(SHERPA_ONNX_C_API_LIBRARY
            NAMES sherpa-onnx-c-api
            HINTS ENV SHERPA_ONNX_ROOT
            PATH_SUFFIXES lib)
        if(SHERPA_ONNX_INCLUDE_DIR AND SHERPA_ONNX_C_API_LIBRARY)
            get_filename_component(NURCADE_SHERPA_LIBRARY_DIR
                "${SHERPA_ONNX_C_API_LIBRARY}" DIRECTORY)
            get_filename_component(NURCADE_SHERPA_ROOT_HINT
                "${NURCADE_SHERPA_LIBRARY_DIR}" DIRECTORY)
            add_library(nuRCADESherpaOnnx UNKNOWN IMPORTED)
            set_target_properties(nuRCADESherpaOnnx PROPERTIES
                IMPORTED_LOCATION "${SHERPA_ONNX_C_API_LIBRARY}"
                INTERFACE_INCLUDE_DIRECTORIES "${SHERPA_ONNX_INCLUDE_DIR}")
            set(NURCADE_SHERPA_ONNX_TARGET nuRCADESherpaOnnx)
            set(NURCADE_SHERPA_ONNX_RUNTIME_DIRS
                "${NURCADE_SHERPA_ROOT_HINT}/bin"
                "${NURCADE_SHERPA_LIBRARY_DIR}")
        endif()
    endif()

    if(NOT NURCADE_SHERPA_ONNX_TARGET
        AND NURCADE_FETCH_NEURAL_TTS
        AND CMAKE_SIZEOF_VOID_P EQUAL 8)
        set(NURCADE_SHERPA_VERSION "1.13.3")
        set(NURCADE_SHERPA_ARCHIVE
            "${CMAKE_BINARY_DIR}/_deps/downloads/sherpa-onnx-${NURCADE_SHERPA_VERSION}.tar.bz2")
        set(NURCADE_SHERPA_EXTRACT_DIR
            "${CMAKE_BINARY_DIR}/_deps/sherpa-onnx-sdk")
        set(NURCADE_SHERPA_ROOT
            "${NURCADE_SHERPA_EXTRACT_DIR}/sherpa-onnx-v${NURCADE_SHERPA_VERSION}-win-x64-shared-MD-Release")
        if(NOT EXISTS "${NURCADE_SHERPA_ARCHIVE}")
            file(MAKE_DIRECTORY "${CMAKE_BINARY_DIR}/_deps/downloads")
            file(DOWNLOAD
                "https://github.com/k2-fsa/sherpa-onnx/releases/download/v${NURCADE_SHERPA_VERSION}/sherpa-onnx-v${NURCADE_SHERPA_VERSION}-win-x64-shared-MD-Release.tar.bz2"
                "${NURCADE_SHERPA_ARCHIVE}"
                EXPECTED_HASH SHA256=6491877a599a4c5a33e5568c8a22f86fc376dc25a2bc49b95373bbf0dd0a12c8
                SHOW_PROGRESS
                TLS_VERIFY ON)
        endif()
        if(NOT EXISTS "${NURCADE_SHERPA_ROOT}/include/sherpa-onnx/c-api/c-api.h")
            file(REMOVE_RECURSE "${NURCADE_SHERPA_EXTRACT_DIR}")
            file(MAKE_DIRECTORY "${NURCADE_SHERPA_EXTRACT_DIR}")
            file(ARCHIVE_EXTRACT
                INPUT "${NURCADE_SHERPA_ARCHIVE}"
                DESTINATION "${NURCADE_SHERPA_EXTRACT_DIR}")
        endif()

        add_library(nuRCADESherpaOnnx SHARED IMPORTED)
        set_target_properties(nuRCADESherpaOnnx PROPERTIES
            IMPORTED_IMPLIB "${NURCADE_SHERPA_ROOT}/lib/sherpa-onnx-c-api.lib"
            IMPORTED_LOCATION "${NURCADE_SHERPA_ROOT}/bin/sherpa-onnx-c-api.dll"
            INTERFACE_INCLUDE_DIRECTORIES "${NURCADE_SHERPA_ROOT}/include")
        set(NURCADE_SHERPA_ONNX_TARGET nuRCADESherpaOnnx)
        set(NURCADE_SHERPA_ONNX_RUNTIME_DIR "${NURCADE_SHERPA_ROOT}/lib")
        set(NURCADE_SHERPA_ONNX_RUNTIME_DIRS
            "${NURCADE_SHERPA_ROOT}/bin"
            "${NURCADE_SHERPA_ROOT}/lib")
    endif()

    if(NURCADE_SHERPA_ONNX_TARGET AND NURCADE_FETCH_NEURAL_TTS)
        set(NURCADE_TTS_MODEL_ARCHIVE
            "${CMAKE_BINARY_DIR}/_deps/downloads/vits-piper-en_GB-alan-medium-int8.tar.bz2")
        set(NURCADE_TTS_MODEL_EXTRACT_DIR
            "${CMAKE_BINARY_DIR}/_deps/sherpa-onnx-model")
        set(NURCADE_TTS_MODEL_DIR
            "${NURCADE_TTS_MODEL_EXTRACT_DIR}/vits-piper-en_GB-alan-medium-int8")
        if(NOT EXISTS "${NURCADE_TTS_MODEL_ARCHIVE}")
            file(MAKE_DIRECTORY "${CMAKE_BINARY_DIR}/_deps/downloads")
            file(DOWNLOAD
                "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/vits-piper-en_GB-alan-medium-int8.tar.bz2"
                "${NURCADE_TTS_MODEL_ARCHIVE}"
                EXPECTED_HASH SHA256=f0b2526c9600657141ccbbe412f6159dceedab5bab4ba4e8857d9443acb50de4
                SHOW_PROGRESS
                TLS_VERIFY ON)
        endif()
        if(NOT EXISTS "${NURCADE_TTS_MODEL_DIR}/en_GB-alan-medium.onnx")
            file(REMOVE_RECURSE "${NURCADE_TTS_MODEL_EXTRACT_DIR}")
            file(MAKE_DIRECTORY "${NURCADE_TTS_MODEL_EXTRACT_DIR}")
            file(ARCHIVE_EXTRACT
                INPUT "${NURCADE_TTS_MODEL_ARCHIVE}"
                DESTINATION "${NURCADE_TTS_MODEL_EXTRACT_DIR}")
        endif()
    endif()
endif()

target_include_directories(nuRCADEWinSupport PUBLIC
    "${CMAKE_CURRENT_SOURCE_DIR}/src/win"
    "${CMAKE_CURRENT_SOURCE_DIR}/src/engine")

target_link_libraries(nuRCADEWinSupport PUBLIC
    nuRCADEEngine
    ddraw
    dxguid
    d2d1
    ole32
    mfplat
    mfreadwrite
    mfuuid
    winmm
    windowscodecs
    sapi)

if(NURCADE_SHERPA_ONNX_TARGET)
    target_link_libraries(nuRCADEWinSupport PRIVATE
        ${NURCADE_SHERPA_ONNX_TARGET})
    target_compile_definitions(nuRCADEWinSupport PRIVATE
        NURCADE_HAS_SHERPA_ONNX_TTS=1)
    message(STATUS "nuRCADE neural TTS: sherpa-onnx enabled")
elseif(NURCADE_ENABLE_NEURAL_TTS)
    message(STATUS "nuRCADE neural TTS: sherpa-onnx not found; using Windows SAPI")
else()
    message(STATUS "nuRCADE neural TTS: disabled; using Windows SAPI")
endif()

target_compile_definitions(nuRCADEWinSupport PUBLIC
    WIN32
    _WINDOWS
    _MBCS
    $<$<CONFIG:Debug>:_DEBUG>
    $<$<NOT:$<CONFIG:Debug>>:NDEBUG>)

add_executable(nuRCADEPlayer WIN32
    ${NURCADE_DEMO_SOURCES}
    ${NURCADE_DEMO_HEADERS}
    nuRCADE-small.ico
    nuRCADE.ico
    ${NURCADE_RESOURCE_FILES})

target_include_directories(nuRCADEPlayer PRIVATE
    "${CMAKE_CURRENT_SOURCE_DIR}"
    "${CMAKE_CURRENT_SOURCE_DIR}/src/app"
    "${CMAKE_CURRENT_SOURCE_DIR}/src/engine"
    "${CMAKE_CURRENT_SOURCE_DIR}/src/win")

target_compile_definitions(nuRCADEPlayer PRIVATE
    WIN32
    _WINDOWS
    _MBCS
    $<$<CONFIG:Debug>:_DEBUG>
    $<$<NOT:$<CONFIG:Debug>>:NDEBUG>)

target_link_libraries(nuRCADEPlayer PRIVATE
    nuRCADEEngine
    nuRCADEWinSupport
    comdlg32
    msimg32
    odbc32
    odbccp32)

set_target_properties(nuRCADEPlayer PROPERTIES
    OUTPUT_NAME "nuRCADEPlayer"
    VS_DEBUGGER_WORKING_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}")

if(NURCADE_SHERPA_ONNX_RUNTIME_DIRS)
    set(NURCADE_SHERPA_ONNX_DLLS "")
    foreach(NURCADE_SHERPA_ONNX_RUNTIME_DIR IN LISTS NURCADE_SHERPA_ONNX_RUNTIME_DIRS)
        file(GLOB NURCADE_SHERPA_ONNX_RUNTIME_DIR_DLLS
            "${NURCADE_SHERPA_ONNX_RUNTIME_DIR}/*.dll")
        list(APPEND NURCADE_SHERPA_ONNX_DLLS ${NURCADE_SHERPA_ONNX_RUNTIME_DIR_DLLS})
    endforeach()
    list(REMOVE_DUPLICATES NURCADE_SHERPA_ONNX_DLLS)
    if(NURCADE_SHERPA_ONNX_DLLS)
        add_custom_command(TARGET nuRCADEPlayer POST_BUILD
            COMMAND "${CMAKE_COMMAND}" -E copy_if_different
                ${NURCADE_SHERPA_ONNX_DLLS}
                "$<TARGET_FILE_DIR:nuRCADEPlayer>"
            VERBATIM)
    endif()
endif()
if(NURCADE_TTS_MODEL_DIR)
    add_custom_command(TARGET nuRCADEPlayer POST_BUILD
        COMMAND "${CMAKE_COMMAND}" -E rm -rf
            "$<TARGET_FILE_DIR:nuRCADEPlayer>/tts-model"
        COMMAND "${CMAKE_COMMAND}" -E make_directory
            "$<TARGET_FILE_DIR:nuRCADEPlayer>/tts-model"
        COMMAND "${CMAKE_COMMAND}" -E copy_directory
            "${NURCADE_TTS_MODEL_DIR}"
            "$<TARGET_FILE_DIR:nuRCADEPlayer>/tts-model"
        VERBATIM)
endif()

if(MSVC)
    target_compile_options(nuRCADEEngine PRIVATE /W3)
    target_compile_options(nuRCADEWinSupport PRIVATE /W3)
    target_compile_options(nuRCADEPlayer PRIVATE /W3)
    target_link_options(nuRCADEPlayer PRIVATE /SUBSYSTEM:WINDOWS)
    set_property(DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}" PROPERTY VS_STARTUP_PROJECT nuRCADEPlayer)
    set_property(GLOBAL PROPERTY USE_FOLDERS ON)
endif()

set_target_properties(nuRCADEEngine     PROPERTIES FOLDER "Engine")
set_target_properties(nuRCADEWinSupport PROPERTIES FOLDER "Engine")
set_target_properties(nuRCADEPlayer     PROPERTIES FOLDER "Engine")

source_group("Engine\\Source Files" FILES
    ${NURCADE_ENGINE_SOURCES})

source_group("Engine\\Header Files" FILES ${NURCADE_ENGINE_HEADERS})
source_group("WinSupport\\Source Files" FILES ${NURCADE_WIN_SUPPORT_SOURCES})
source_group("WinSupport\\Header Files" FILES ${NURCADE_WIN_SUPPORT_HEADERS})
source_group("Demo\\Source Files" FILES ${NURCADE_DEMO_SOURCES})
source_group("Demo\\Header Files" FILES ${NURCADE_DEMO_HEADERS})
source_group("Resource Files" FILES
    nuRCADE.rc
    nuRCADE-small.ico
    nuRCADE.ico
    ${NURCADE_RESOURCE_FILES})

add_custom_target(nuRCADEPackageResources
    DEPENDS ${NURCADE_RESOURCE_FILES})
set_target_properties(nuRCADEPackageResources PROPERTIES FOLDER "Packaging")

###############################################################################
# Editor (.NET 8 WPF) — published as a self-contained Win-x64 app
###############################################################################

set(NURCADE_EDITOR_PROJECT
    "${CMAKE_CURRENT_SOURCE_DIR}/tools/NuRcade.Editor/src/NuRcade.Editor/NuRcade.Editor.csproj")
set(NURCADE_EDITOR_PUBLISH_DIR
    "${CMAKE_BINARY_DIR}/editor_publish")
set(NURCADE_EDITOR_PUBLISH_STAMP
    "${CMAKE_BINARY_DIR}/editor_publish.stamp")
set(NURCADE_EDITOR_EXE
    "${NURCADE_EDITOR_PUBLISH_DIR}/NuRcade.Editor.exe")

find_program(DOTNET_EXECUTABLE dotnet)
if(DOTNET_EXECUTABLE)
    file(GLOB NURCADE_EDITOR_SOURCES CONFIGURE_DEPENDS
        "${CMAKE_CURRENT_SOURCE_DIR}/tools/NuRcade.Editor/src/NuRcade.Editor/*.cs"
        "${CMAKE_CURRENT_SOURCE_DIR}/tools/NuRcade.Editor/src/NuRcade.Editor/*.csproj"
        "${CMAKE_CURRENT_SOURCE_DIR}/tools/NuRcade.Editor/src/NuRcade.Editor/*.xaml"
        "${CMAKE_CURRENT_SOURCE_DIR}/tools/NuRcade.Editor/src/NuRcade.Editor.Core/*.cs"
        "${CMAKE_CURRENT_SOURCE_DIR}/tools/NuRcade.Editor/src/NuRcade.Editor.Core/*.csproj"
        "${CMAKE_CURRENT_SOURCE_DIR}/tools/NuRcade.Editor/src/NuRcade.Editor.Tests/*.cs"
        "${CMAKE_CURRENT_SOURCE_DIR}/tools/NuRcade.Editor/src/NuRcade.Editor.Tests/*.csproj")

    add_custom_command(
        OUTPUT "${NURCADE_EDITOR_PUBLISH_STAMP}"
        COMMAND "${CMAKE_COMMAND}" -E rm -rf "${NURCADE_EDITOR_PUBLISH_DIR}"
        COMMAND "${DOTNET_EXECUTABLE}" publish "${NURCADE_EDITOR_PROJECT}"
            -c Release
            -r win-x64
            --self-contained true
            -p:DebugType=None
            -p:DebugSymbols=false
            -o "${NURCADE_EDITOR_PUBLISH_DIR}"
        COMMAND "${CMAKE_COMMAND}" -E touch "${NURCADE_EDITOR_PUBLISH_STAMP}"
        DEPENDS ${NURCADE_EDITOR_SOURCES}
        COMMENT "Publishing nuRCADE Editor (self-contained Win-x64) for packaging"
        VERBATIM)

    add_custom_target(NuRcade.EditorPublish ALL
        DEPENDS "${NURCADE_EDITOR_PUBLISH_STAMP}")

    add_custom_target(NuRcade.EditorPublishFresh
        COMMAND "${CMAKE_COMMAND}" -E rm -rf "${NURCADE_EDITOR_PUBLISH_DIR}"
        COMMAND "${DOTNET_EXECUTABLE}" publish "${NURCADE_EDITOR_PROJECT}"
            -c Release
            -r win-x64
            --self-contained true
            -p:DebugType=None
            -p:DebugSymbols=false
            -o "${NURCADE_EDITOR_PUBLISH_DIR}"
        COMMAND "${CMAKE_COMMAND}" -E touch "${NURCADE_EDITOR_PUBLISH_STAMP}"
        DEPENDS ${NURCADE_EDITOR_SOURCES}
        COMMENT "Fresh-publishing nuRCADE Editor for installer packaging"
        VERBATIM)

    add_dependencies(NuRcade.EditorPublishFresh NuRcade.EditorPublish)

    set_target_properties(NuRcade.EditorPublishFresh PROPERTIES
        FOLDER "Packaging"
        EXCLUDE_FROM_DEFAULT_BUILD TRUE)

    # Surface the editor C# projects inside the Visual Studio solution so the
    # whole workspace (engine + editor + installer) lives in nuRCADE.sln.
    if(CMAKE_GENERATOR MATCHES "Visual Studio")
        set(NURCADE_NETSDK_PROJECT_GUID
            "9A19103F-16F7-4668-BE54-9A1E7A4F7556")

        include_external_msproject(NuRcade.Editor.Core
            "${CMAKE_CURRENT_SOURCE_DIR}/tools/NuRcade.Editor/src/NuRcade.Editor.Core/NuRcade.Editor.Core.csproj"
            TYPE ${NURCADE_NETSDK_PROJECT_GUID}
            PLATFORM "Any CPU")

        include_external_msproject(NuRcade.Editor
            "${CMAKE_CURRENT_SOURCE_DIR}/tools/NuRcade.Editor/src/NuRcade.Editor/NuRcade.Editor.csproj"
            TYPE ${NURCADE_NETSDK_PROJECT_GUID}
            PLATFORM "Any CPU"
            NuRcade.Editor.Core)

        if(BUILD_TESTING)
            include_external_msproject(NuRcade.Editor.Tests
                "${CMAKE_CURRENT_SOURCE_DIR}/tools/NuRcade.Editor/src/NuRcade.Editor.Tests/NuRcade.Editor.Tests.csproj"
                TYPE ${NURCADE_NETSDK_PROJECT_GUID}
                PLATFORM "Any CPU"
                NuRcade.Editor.Core
                NuRcade.Editor)
        endif()

        set_target_properties(NuRcade.Editor.Core PROPERTIES
            FOLDER "Editor"
            EXCLUDE_FROM_ALL TRUE
            EXCLUDE_FROM_DEFAULT_BUILD TRUE)
        set_target_properties(NuRcade.Editor PROPERTIES
            FOLDER "Editor"
            EXCLUDE_FROM_ALL TRUE
            EXCLUDE_FROM_DEFAULT_BUILD TRUE)
        if(BUILD_TESTING)
            set_target_properties(NuRcade.Editor.Tests PROPERTIES
                FOLDER "Editor"
                EXCLUDE_FROM_ALL TRUE
                EXCLUDE_FROM_DEFAULT_BUILD TRUE)
        endif()
    endif()
else()
    message(WARNING "dotnet not found in PATH — editor will be skipped during packaging.")
    add_custom_target(NuRcade.EditorPublish
        COMMAND "${CMAKE_COMMAND}" -E echo
            "Skipping editor publish: dotnet not found. Install .NET 8 SDK to package the editor.")
endif()

###############################################################################
# Install rules — used by the standalone installer (CPack/NSIS)
###############################################################################

install(TARGETS nuRCADEPlayer
    RUNTIME DESTINATION .
    COMPONENT engine)

if(NURCADE_SHERPA_ONNX_DLLS)
    install(FILES ${NURCADE_SHERPA_ONNX_DLLS}
        DESTINATION .
        COMPONENT engine)
endif()
if(NURCADE_TTS_MODEL_DIR)
    install(DIRECTORY "${NURCADE_TTS_MODEL_DIR}/"
        DESTINATION tts-model
        COMPONENT engine)
endif()

install(DIRECTORY res/
    DESTINATION res
    COMPONENT engine
    FILES_MATCHING
        PATTERN "*.json"
        PATTERN "*.md"
        PATTERN "*.bmp"
        PATTERN "*.png"
        PATTERN "*.mp3"
        PATTERN "*.ogg"
        PATTERN "*.oga"
        PATTERN "*.wav"
        PATTERN "*.wma"
        PATTERN "*.mid"
        PATTERN "*.midi"
        PATTERN "*.txt")

install(FILES "${CMAKE_CURRENT_SOURCE_DIR}/COPYING"
    DESTINATION .
    RENAME LICENSE.txt
    COMPONENT engine)

if(DOTNET_EXECUTABLE)
    install(CODE "
        if(NOT EXISTS \"${NURCADE_EDITOR_EXE}\")
            message(FATAL_ERROR
                \"NuRcade.Editor.exe not found at ${NURCADE_EDITOR_EXE}. \"
                \"Build the NuRcade.EditorPublish target before running cpack.\")
        endif()
    " COMPONENT editor)

    install(DIRECTORY "${NURCADE_EDITOR_PUBLISH_DIR}/"
        DESTINATION editor
        COMPONENT editor
        USE_SOURCE_PERMISSIONS
        PATTERN "*.pdb" EXCLUDE)
endif()

###############################################################################
# CPack — NSIS installer
###############################################################################

set(CPACK_PACKAGE_NAME              "nuRCADE")
set(CPACK_PACKAGE_VENDOR            "Antonino Calderone")
set(CPACK_PACKAGE_CONTACT           "antonino.calderone@gmail.com")
set(CPACK_PACKAGE_DESCRIPTION_SUMMARY
    "nuRCADE - New (nu) Raycasting Classic Arcade Development Engine")
set(CPACK_PACKAGE_VERSION           "${PROJECT_VERSION}")
set(CPACK_PACKAGE_INSTALL_DIRECTORY "nuRCADE")
set(CPACK_RESOURCE_FILE_LICENSE     "${CMAKE_CURRENT_SOURCE_DIR}/COPYING")
set(CPACK_PACKAGE_FILE_NAME
    "nuRCADE-${PROJECT_VERSION}-win-x64-setup")

if(NOT CPACK_GENERATOR)
    set(CPACK_GENERATOR "NSIS;ZIP")
endif()
set(CPACK_NSIS_DISPLAY_NAME         "nuRCADE ${PROJECT_VERSION}")
set(CPACK_NSIS_PACKAGE_NAME         "nuRCADE ${PROJECT_VERSION}")
set(CPACK_NSIS_HELP_LINK            "https://github.com/eantcal/nurcade")
set(CPACK_NSIS_URL_INFO_ABOUT       "https://github.com/eantcal/nurcade")
set(CPACK_NSIS_CONTACT              "${CPACK_PACKAGE_CONTACT}")
set(CPACK_NSIS_MODIFY_PATH          OFF)
set(CPACK_NSIS_ENABLE_UNINSTALL_BEFORE_INSTALL ON)
set(CPACK_NSIS_MUI_ICON
    "${CMAKE_CURRENT_SOURCE_DIR}/tools/NuRcade.Editor/src/NuRcade.Editor/Assets/AppIcon.ico")
set(CPACK_NSIS_MUI_UNIICON
    "${CMAKE_CURRENT_SOURCE_DIR}/tools/NuRcade.Editor/src/NuRcade.Editor/Assets/AppIcon.ico")

set(CPACK_NSIS_MUI_FINISHPAGE_RUN "nuRCADEPlayer.exe")

set(CPACK_NSIS_CREATE_ICONS_EXTRA
    "CreateShortCut '$SMPROGRAMS\\\\$STARTMENU_FOLDER\\\\nuRCADE Player (demo world).lnk' '$INSTDIR\\\\nuRCADEPlayer.exe' '$\\\"$INSTDIR\\\\res\\\\worlds\\\\demo_embedded\\\\demo.world.json$\\\"' '$INSTDIR\\\\nuRCADEPlayer.exe' 0 SW_SHOWNORMAL '' 'Run the self-contained demo world'
    CreateShortCut '$SMPROGRAMS\\\\$STARTMENU_FOLDER\\\\nuRCADE Player.lnk' '$INSTDIR\\\\nuRCADEPlayer.exe' '' '$INSTDIR\\\\nuRCADEPlayer.exe' 0 SW_SHOWNORMAL '' 'Run the default nuRCADE world'
    CreateShortCut '$SMPROGRAMS\\\\$STARTMENU_FOLDER\\\\nuRCADE Editor.lnk' '$INSTDIR\\\\editor\\\\NuRcade.Editor.exe' '' '$INSTDIR\\\\editor\\\\NuRcade.Editor.exe' 0 SW_SHOWNORMAL '' 'Author worlds, blocks, and sprites for nuRCADE'")
set(CPACK_NSIS_DELETE_ICONS_EXTRA
    "Delete '$SMPROGRAMS\\\\$STARTMENU_FOLDER\\\\nuRCADE Player (demo world).lnk'
    Delete '$SMPROGRAMS\\\\$STARTMENU_FOLDER\\\\nuRCADE Player.lnk'
    Delete '$SMPROGRAMS\\\\$STARTMENU_FOLDER\\\\nuRCADE Editor.lnk'")

set(CPACK_COMPONENTS_ALL engine)
if(DOTNET_EXECUTABLE)
    list(APPEND CPACK_COMPONENTS_ALL editor)
endif()

set(CPACK_COMPONENT_ENGINE_DISPLAY_NAME "Engine and demo world")
set(CPACK_COMPONENT_ENGINE_DESCRIPTION
    "nuRCADE Player and self-contained world packages with maps, textures, and sprites.")
set(CPACK_COMPONENT_ENGINE_REQUIRED ON)

set(CPACK_COMPONENT_EDITOR_DISPLAY_NAME "nuRCADE Editor")
set(CPACK_COMPONENT_EDITOR_DESCRIPTION
    "Self-contained .NET 8 WPF authoring tool for maps, blocks, and sprites.")

include(CPack)

set(NURCADE_PACKAGE_DEPENDENCIES
    nuRCADEPlayer
    nuRCADEPackageResources)
if(DOTNET_EXECUTABLE)
    list(APPEND NURCADE_PACKAGE_DEPENDENCIES NuRcade.EditorPublishFresh)
endif()

foreach(NURCADE_CPACK_TARGET package PACKAGE)
    if(TARGET ${NURCADE_CPACK_TARGET})
        add_dependencies(
            ${NURCADE_CPACK_TARGET}
            ${NURCADE_PACKAGE_DEPENDENCIES})
    endif()
endforeach()

add_custom_target(nuRCADEFreshPackage
    COMMAND "${CMAKE_COMMAND}" -E rm -rf "${CMAKE_BINARY_DIR}/_CPack_Packages"
    COMMAND "${CMAKE_CPACK_COMMAND}"
        -C "$<CONFIG>"
        --config "${CMAKE_BINARY_DIR}/CPackConfig.cmake"
    DEPENDS ${NURCADE_PACKAGE_DEPENDENCIES} ${NURCADE_RESOURCE_FILES}
    COMMENT "Creating a fresh nuRCADE installer/package from rebuilt binaries and current resources"
    VERBATIM)

set_target_properties(nuRCADEFreshPackage PROPERTIES FOLDER "Packaging")

if(BUILD_TESTING)
    set(gtest_force_shared_crt ON CACHE BOOL "" FORCE)
    set(INSTALL_GTEST OFF CACHE BOOL "" FORCE)
    set(INSTALL_GMOCK OFF CACHE BOOL "" FORCE)
    FetchContent_Declare(
        googletest
        URL https://github.com/google/googletest/archive/refs/tags/v1.14.0.zip
        DOWNLOAD_EXTRACT_TIMESTAMP TRUE)
    FetchContent_MakeAvailable(googletest)

    add_executable(nuRCADETests
        tests/ActorSystemTests.cpp
        tests/ColumnDepthBufferTests.cpp
        tests/FrameBufferTests.cpp
        tests/MapCellTests.cpp
        tests/RayCasterTests.cpp
        tests/RaycastEngineSpriteTests.cpp
        tests/RenderRegressionTests.cpp
        tests/SceneLoaderTests.cpp
        tests/SpriteMetadataLoaderTests.cpp
        tests/SpriteSetTests.cpp
        tests/SpriteProjectionTests.cpp
        tests/SpriteTests.cpp
        tests/TextureTests.cpp
        tests/ViewWeaponTests.cpp
        tests/WorldJsonLoaderTests.cpp
        tests/BenchmarkScene.h)

    target_include_directories(nuRCADETests PRIVATE
        "${CMAKE_CURRENT_SOURCE_DIR}/src/engine"
        "${CMAKE_CURRENT_SOURCE_DIR}/src/win"
        "${CMAKE_CURRENT_SOURCE_DIR}/src/app")

    target_compile_definitions(nuRCADETests PRIVATE
        NURCADE_REPO_DIR="${CMAKE_CURRENT_SOURCE_DIR}")

    target_link_libraries(nuRCADETests PRIVATE
        nuRCADEEngine
        GTest::gtest_main)

    include(GoogleTest)
    gtest_discover_tests(nuRCADETests
        DISCOVERY_MODE PRE_TEST)

    set_target_properties(nuRCADETests PROPERTIES FOLDER "Engine/Tests")

    # Offscreen rendering benchmark (standalone console app, not a ctest).
    add_executable(nuRCADEBenchmark
        tests/RenderBenchmarkMain.cpp
        tests/BenchmarkScene.h)

    target_include_directories(nuRCADEBenchmark PRIVATE
        "${CMAKE_CURRENT_SOURCE_DIR}/src/engine")

    target_link_libraries(nuRCADEBenchmark PRIVATE nuRCADEEngine)
    set_target_properties(nuRCADEBenchmark PROPERTIES FOLDER "Engine/Tests")

    source_group("Tests" FILES
        tests/ActorSystemTests.cpp
        tests/ColumnDepthBufferTests.cpp
        tests/FrameBufferTests.cpp
        tests/MapCellTests.cpp
        tests/RayCasterTests.cpp
        tests/RaycastEngineSpriteTests.cpp
        tests/RenderRegressionTests.cpp
        tests/SceneLoaderTests.cpp
        tests/SpriteMetadataLoaderTests.cpp
        tests/SpriteSetTests.cpp
        tests/SpriteProjectionTests.cpp
        tests/SpriteTests.cpp
        tests/TextureTests.cpp
        tests/ViewWeaponTests.cpp
        tests/WorldJsonLoaderTests.cpp
        tests/BenchmarkScene.h
        tests/RenderBenchmarkMain.cpp)
endif()
