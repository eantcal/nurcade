cmake_minimum_required(VERSION 3.20)

project(WinRayCast
    VERSION 1.2.0
    DESCRIPTION "Classic software ray-casting engine demo for Windows"
    LANGUAGES CXX RC)

if(NOT WIN32)
    message(FATAL_ERROR "WinRayCast currently targets Windows only.")
endif()

include(CTest)
include(FetchContent)

option(WINRAYCAST_ENABLE_NEURAL_TTS
    "Use sherpa-onnx neural TTS when the library and a runtime model are available"
    ON)
option(WINRAYCAST_FETCH_NEURAL_TTS
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

set(WINRAYCAST_ENGINE_SOURCES
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

set(WINRAYCAST_ENGINE_HEADERS
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

set(WINRAYCAST_WIN_SUPPORT_SOURCES
    src/win/BackgroundMusicPlayer.cpp
    src/win/D2dPresenter.cpp
    src/win/DdxDevice.cpp
    src/win/OggVorbisDecoder.cpp
    src/win/SoundEffectPlayer.cpp
    src/win/TextToSpeechPlayer.cpp
    src/win/WinFramePresenter.cpp
    src/win/WinTextureLoader.cpp)

set(WINRAYCAST_WIN_SUPPORT_HEADERS
    src/win/BackgroundMusicPlayer.h
    src/win/D2dPresenter.h
    src/win/DdxDevice.h
    src/win/OggVorbisDecoder.h
    src/win/SoundEffectPlayer.h
    src/win/TextToSpeechPlayer.h
    src/win/WinFramePresenter.h
    src/win/WinTextureLoader.h)

set(WINRAYCAST_DEMO_SOURCES
    src/app/WinRayCast.cpp
    WinRayCast.rc)

set(WINRAYCAST_DEMO_HEADERS
    src/app/3ddemoconfig.h
    src/app/WinRayCast.h
    resource.h)

file(GLOB_RECURSE WINRAYCAST_RESOURCE_FILES CONFIGURE_DEPENDS
    "${CMAKE_CURRENT_SOURCE_DIR}/res/worlds/*"
    "${CMAKE_CURRENT_SOURCE_DIR}/res/examples/*")

add_library(WinRayCastEngine STATIC
    ${WINRAYCAST_ENGINE_SOURCES}
    ${WINRAYCAST_ENGINE_HEADERS})

target_include_directories(WinRayCastEngine PUBLIC
    "${CMAKE_CURRENT_SOURCE_DIR}/src/engine")

target_compile_definitions(WinRayCastEngine PUBLIC
    WIN32
    _MBCS
    $<$<CONFIG:Debug>:_DEBUG>
    $<$<NOT:$<CONFIG:Debug>>:NDEBUG>)

target_link_libraries(WinRayCastEngine PUBLIC
    nlohmann_json::nlohmann_json)

add_library(WinRayCastWinSupport STATIC
    ${WINRAYCAST_WIN_SUPPORT_SOURCES}
    ${WINRAYCAST_WIN_SUPPORT_HEADERS})

set(WINRAYCAST_SHERPA_ONNX_TARGET "")
set(WINRAYCAST_SHERPA_ONNX_RUNTIME_DIR "")
set(WINRAYCAST_SHERPA_ONNX_RUNTIME_DIRS "")
set(WINRAYCAST_TTS_MODEL_DIR "")
if(WINRAYCAST_ENABLE_NEURAL_TTS)
    find_package(PkgConfig QUIET)
    if(PkgConfig_FOUND)
        pkg_check_modules(SHERPA_ONNX QUIET IMPORTED_TARGET sherpa-onnx)
        if(TARGET PkgConfig::SHERPA_ONNX)
            set(WINRAYCAST_SHERPA_ONNX_TARGET PkgConfig::SHERPA_ONNX)
        endif()
    endif()

    if(NOT WINRAYCAST_SHERPA_ONNX_TARGET)
        find_path(SHERPA_ONNX_INCLUDE_DIR
            NAMES sherpa-onnx/c-api/c-api.h
            HINTS ENV SHERPA_ONNX_ROOT
            PATH_SUFFIXES include)
        find_library(SHERPA_ONNX_C_API_LIBRARY
            NAMES sherpa-onnx-c-api
            HINTS ENV SHERPA_ONNX_ROOT
            PATH_SUFFIXES lib)
        if(SHERPA_ONNX_INCLUDE_DIR AND SHERPA_ONNX_C_API_LIBRARY)
            get_filename_component(WINRAYCAST_SHERPA_LIBRARY_DIR
                "${SHERPA_ONNX_C_API_LIBRARY}" DIRECTORY)
            get_filename_component(WINRAYCAST_SHERPA_ROOT_HINT
                "${WINRAYCAST_SHERPA_LIBRARY_DIR}" DIRECTORY)
            add_library(WinRayCastSherpaOnnx UNKNOWN IMPORTED)
            set_target_properties(WinRayCastSherpaOnnx PROPERTIES
                IMPORTED_LOCATION "${SHERPA_ONNX_C_API_LIBRARY}"
                INTERFACE_INCLUDE_DIRECTORIES "${SHERPA_ONNX_INCLUDE_DIR}")
            set(WINRAYCAST_SHERPA_ONNX_TARGET WinRayCastSherpaOnnx)
            set(WINRAYCAST_SHERPA_ONNX_RUNTIME_DIRS
                "${WINRAYCAST_SHERPA_ROOT_HINT}/bin"
                "${WINRAYCAST_SHERPA_LIBRARY_DIR}")
        endif()
    endif()

    if(NOT WINRAYCAST_SHERPA_ONNX_TARGET
        AND WINRAYCAST_FETCH_NEURAL_TTS
        AND CMAKE_SIZEOF_VOID_P EQUAL 8)
        set(WINRAYCAST_SHERPA_VERSION "1.13.3")
        set(WINRAYCAST_SHERPA_ARCHIVE
            "${CMAKE_BINARY_DIR}/_deps/downloads/sherpa-onnx-${WINRAYCAST_SHERPA_VERSION}.tar.bz2")
        set(WINRAYCAST_SHERPA_EXTRACT_DIR
            "${CMAKE_BINARY_DIR}/_deps/sherpa-onnx-sdk")
        set(WINRAYCAST_SHERPA_ROOT
            "${WINRAYCAST_SHERPA_EXTRACT_DIR}/sherpa-onnx-v${WINRAYCAST_SHERPA_VERSION}-win-x64-shared-MD-Release")
        if(NOT EXISTS "${WINRAYCAST_SHERPA_ARCHIVE}")
            file(MAKE_DIRECTORY "${CMAKE_BINARY_DIR}/_deps/downloads")
            file(DOWNLOAD
                "https://github.com/k2-fsa/sherpa-onnx/releases/download/v${WINRAYCAST_SHERPA_VERSION}/sherpa-onnx-v${WINRAYCAST_SHERPA_VERSION}-win-x64-shared-MD-Release.tar.bz2"
                "${WINRAYCAST_SHERPA_ARCHIVE}"
                EXPECTED_HASH SHA256=6491877a599a4c5a33e5568c8a22f86fc376dc25a2bc49b95373bbf0dd0a12c8
                SHOW_PROGRESS
                TLS_VERIFY ON)
        endif()
        if(NOT EXISTS "${WINRAYCAST_SHERPA_ROOT}/include/sherpa-onnx/c-api/c-api.h")
            file(REMOVE_RECURSE "${WINRAYCAST_SHERPA_EXTRACT_DIR}")
            file(MAKE_DIRECTORY "${WINRAYCAST_SHERPA_EXTRACT_DIR}")
            file(ARCHIVE_EXTRACT
                INPUT "${WINRAYCAST_SHERPA_ARCHIVE}"
                DESTINATION "${WINRAYCAST_SHERPA_EXTRACT_DIR}")
        endif()

        add_library(WinRayCastSherpaOnnx SHARED IMPORTED)
        set_target_properties(WinRayCastSherpaOnnx PROPERTIES
            IMPORTED_IMPLIB "${WINRAYCAST_SHERPA_ROOT}/lib/sherpa-onnx-c-api.lib"
            IMPORTED_LOCATION "${WINRAYCAST_SHERPA_ROOT}/bin/sherpa-onnx-c-api.dll"
            INTERFACE_INCLUDE_DIRECTORIES "${WINRAYCAST_SHERPA_ROOT}/include")
        set(WINRAYCAST_SHERPA_ONNX_TARGET WinRayCastSherpaOnnx)
        set(WINRAYCAST_SHERPA_ONNX_RUNTIME_DIR "${WINRAYCAST_SHERPA_ROOT}/lib")
        set(WINRAYCAST_SHERPA_ONNX_RUNTIME_DIRS
            "${WINRAYCAST_SHERPA_ROOT}/bin"
            "${WINRAYCAST_SHERPA_ROOT}/lib")
    endif()

    if(WINRAYCAST_SHERPA_ONNX_TARGET AND WINRAYCAST_FETCH_NEURAL_TTS)
        set(WINRAYCAST_TTS_MODEL_ARCHIVE
            "${CMAKE_BINARY_DIR}/_deps/downloads/vits-piper-en_GB-alan-medium-int8.tar.bz2")
        set(WINRAYCAST_TTS_MODEL_EXTRACT_DIR
            "${CMAKE_BINARY_DIR}/_deps/sherpa-onnx-model")
        set(WINRAYCAST_TTS_MODEL_DIR
            "${WINRAYCAST_TTS_MODEL_EXTRACT_DIR}/vits-piper-en_GB-alan-medium-int8")
        if(NOT EXISTS "${WINRAYCAST_TTS_MODEL_ARCHIVE}")
            file(MAKE_DIRECTORY "${CMAKE_BINARY_DIR}/_deps/downloads")
            file(DOWNLOAD
                "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/vits-piper-en_GB-alan-medium-int8.tar.bz2"
                "${WINRAYCAST_TTS_MODEL_ARCHIVE}"
                EXPECTED_HASH SHA256=f0b2526c9600657141ccbbe412f6159dceedab5bab4ba4e8857d9443acb50de4
                SHOW_PROGRESS
                TLS_VERIFY ON)
        endif()
        if(NOT EXISTS "${WINRAYCAST_TTS_MODEL_DIR}/en_GB-alan-medium.onnx")
            file(REMOVE_RECURSE "${WINRAYCAST_TTS_MODEL_EXTRACT_DIR}")
            file(MAKE_DIRECTORY "${WINRAYCAST_TTS_MODEL_EXTRACT_DIR}")
            file(ARCHIVE_EXTRACT
                INPUT "${WINRAYCAST_TTS_MODEL_ARCHIVE}"
                DESTINATION "${WINRAYCAST_TTS_MODEL_EXTRACT_DIR}")
        endif()
    endif()
endif()

target_include_directories(WinRayCastWinSupport PUBLIC
    "${CMAKE_CURRENT_SOURCE_DIR}/src/win"
    "${CMAKE_CURRENT_SOURCE_DIR}/src/engine")

target_link_libraries(WinRayCastWinSupport PUBLIC
    WinRayCastEngine
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

if(WINRAYCAST_SHERPA_ONNX_TARGET)
    target_link_libraries(WinRayCastWinSupport PRIVATE
        ${WINRAYCAST_SHERPA_ONNX_TARGET})
    target_compile_definitions(WinRayCastWinSupport PRIVATE
        WINRAYCAST_HAS_SHERPA_ONNX_TTS=1)
    message(STATUS "WinRayCast neural TTS: sherpa-onnx enabled")
elseif(WINRAYCAST_ENABLE_NEURAL_TTS)
    message(STATUS "WinRayCast neural TTS: sherpa-onnx not found; using Windows SAPI")
else()
    message(STATUS "WinRayCast neural TTS: disabled; using Windows SAPI")
endif()

target_compile_definitions(WinRayCastWinSupport PUBLIC
    WIN32
    _WINDOWS
    _MBCS
    $<$<CONFIG:Debug>:_DEBUG>
    $<$<NOT:$<CONFIG:Debug>>:NDEBUG>)

add_executable(WinRayCastPlayer WIN32
    ${WINRAYCAST_DEMO_SOURCES}
    ${WINRAYCAST_DEMO_HEADERS}
    small.ico
    WinRayCast.ico
    ${WINRAYCAST_RESOURCE_FILES})

target_include_directories(WinRayCastPlayer PRIVATE
    "${CMAKE_CURRENT_SOURCE_DIR}"
    "${CMAKE_CURRENT_SOURCE_DIR}/src/app"
    "${CMAKE_CURRENT_SOURCE_DIR}/src/engine"
    "${CMAKE_CURRENT_SOURCE_DIR}/src/win")

target_compile_definitions(WinRayCastPlayer PRIVATE
    WIN32
    _WINDOWS
    _MBCS
    $<$<CONFIG:Debug>:_DEBUG>
    $<$<NOT:$<CONFIG:Debug>>:NDEBUG>)

target_link_libraries(WinRayCastPlayer PRIVATE
    WinRayCastEngine
    WinRayCastWinSupport
    comdlg32
    msimg32
    odbc32
    odbccp32)

set_target_properties(WinRayCastPlayer PROPERTIES
    OUTPUT_NAME "WinRayCastPlayer"
    VS_DEBUGGER_WORKING_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}")

if(WINRAYCAST_SHERPA_ONNX_RUNTIME_DIRS)
    set(WINRAYCAST_SHERPA_ONNX_DLLS "")
    foreach(WINRAYCAST_SHERPA_ONNX_RUNTIME_DIR IN LISTS WINRAYCAST_SHERPA_ONNX_RUNTIME_DIRS)
        file(GLOB WINRAYCAST_SHERPA_ONNX_RUNTIME_DIR_DLLS
            "${WINRAYCAST_SHERPA_ONNX_RUNTIME_DIR}/*.dll")
        list(APPEND WINRAYCAST_SHERPA_ONNX_DLLS ${WINRAYCAST_SHERPA_ONNX_RUNTIME_DIR_DLLS})
    endforeach()
    list(REMOVE_DUPLICATES WINRAYCAST_SHERPA_ONNX_DLLS)
    if(WINRAYCAST_SHERPA_ONNX_DLLS)
        add_custom_command(TARGET WinRayCastPlayer POST_BUILD
            COMMAND "${CMAKE_COMMAND}" -E copy_if_different
                ${WINRAYCAST_SHERPA_ONNX_DLLS}
                "$<TARGET_FILE_DIR:WinRayCastPlayer>"
            VERBATIM)
    endif()
endif()
if(WINRAYCAST_TTS_MODEL_DIR)
    add_custom_command(TARGET WinRayCastPlayer POST_BUILD
        COMMAND "${CMAKE_COMMAND}" -E rm -rf
            "$<TARGET_FILE_DIR:WinRayCastPlayer>/tts-model"
        COMMAND "${CMAKE_COMMAND}" -E make_directory
            "$<TARGET_FILE_DIR:WinRayCastPlayer>/tts-model"
        COMMAND "${CMAKE_COMMAND}" -E copy_directory
            "${WINRAYCAST_TTS_MODEL_DIR}"
            "$<TARGET_FILE_DIR:WinRayCastPlayer>/tts-model"
        VERBATIM)
endif()

if(MSVC)
    target_compile_options(WinRayCastEngine PRIVATE /W3)
    target_compile_options(WinRayCastWinSupport PRIVATE /W3)
    target_compile_options(WinRayCastPlayer PRIVATE /W3)
    target_link_options(WinRayCastPlayer PRIVATE /SUBSYSTEM:WINDOWS)
    set_property(DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}" PROPERTY VS_STARTUP_PROJECT WinRayCastPlayer)
    set_property(GLOBAL PROPERTY USE_FOLDERS ON)
endif()

set_target_properties(WinRayCastEngine     PROPERTIES FOLDER "Engine")
set_target_properties(WinRayCastWinSupport PROPERTIES FOLDER "Engine")
set_target_properties(WinRayCastPlayer     PROPERTIES FOLDER "Engine")

source_group("Engine\\Source Files" FILES
    ${WINRAYCAST_ENGINE_SOURCES})

source_group("Engine\\Header Files" FILES ${WINRAYCAST_ENGINE_HEADERS})
source_group("WinSupport\\Source Files" FILES ${WINRAYCAST_WIN_SUPPORT_SOURCES})
source_group("WinSupport\\Header Files" FILES ${WINRAYCAST_WIN_SUPPORT_HEADERS})
source_group("Demo\\Source Files" FILES ${WINRAYCAST_DEMO_SOURCES})
source_group("Demo\\Header Files" FILES ${WINRAYCAST_DEMO_HEADERS})
source_group("Resource Files" FILES
    WinRayCast.rc
    small.ico
    WinRayCast.ico
    ${WINRAYCAST_RESOURCE_FILES})

add_custom_target(WinRayCastPackageResources
    DEPENDS ${WINRAYCAST_RESOURCE_FILES})
set_target_properties(WinRayCastPackageResources PROPERTIES FOLDER "Packaging")

###############################################################################
# Editor (.NET 8 WPF) — published as a self-contained Win-x64 app
###############################################################################

set(WINRAYCAST_EDITOR_PROJECT
    "${CMAKE_CURRENT_SOURCE_DIR}/tools/WinRaycastEditor/src/WinRaycastEditor/WinRaycastEditor.csproj")
set(WINRAYCAST_EDITOR_PUBLISH_DIR
    "${CMAKE_BINARY_DIR}/editor_publish")
set(WINRAYCAST_EDITOR_PUBLISH_STAMP
    "${CMAKE_BINARY_DIR}/editor_publish.stamp")
set(WINRAYCAST_EDITOR_EXE
    "${WINRAYCAST_EDITOR_PUBLISH_DIR}/WinRaycastEditor.exe")

find_program(DOTNET_EXECUTABLE dotnet)
if(DOTNET_EXECUTABLE)
    file(GLOB WINRAYCAST_EDITOR_SOURCES CONFIGURE_DEPENDS
        "${CMAKE_CURRENT_SOURCE_DIR}/tools/WinRaycastEditor/src/WinRaycastEditor/*.cs"
        "${CMAKE_CURRENT_SOURCE_DIR}/tools/WinRaycastEditor/src/WinRaycastEditor/*.csproj"
        "${CMAKE_CURRENT_SOURCE_DIR}/tools/WinRaycastEditor/src/WinRaycastEditor/*.xaml"
        "${CMAKE_CURRENT_SOURCE_DIR}/tools/WinRaycastEditor/src/WinRaycastEditor.Core/*.cs"
        "${CMAKE_CURRENT_SOURCE_DIR}/tools/WinRaycastEditor/src/WinRaycastEditor.Core/*.csproj"
        "${CMAKE_CURRENT_SOURCE_DIR}/tools/WinRaycastEditor/src/WinRaycastEditor.Tests/*.cs"
        "${CMAKE_CURRENT_SOURCE_DIR}/tools/WinRaycastEditor/src/WinRaycastEditor.Tests/*.csproj")

    add_custom_command(
        OUTPUT "${WINRAYCAST_EDITOR_PUBLISH_STAMP}"
        COMMAND "${CMAKE_COMMAND}" -E rm -rf "${WINRAYCAST_EDITOR_PUBLISH_DIR}"
        COMMAND "${DOTNET_EXECUTABLE}" publish "${WINRAYCAST_EDITOR_PROJECT}"
            -c Release
            -r win-x64
            --self-contained true
            -p:DebugType=None
            -p:DebugSymbols=false
            -o "${WINRAYCAST_EDITOR_PUBLISH_DIR}"
        COMMAND "${CMAKE_COMMAND}" -E touch "${WINRAYCAST_EDITOR_PUBLISH_STAMP}"
        DEPENDS ${WINRAYCAST_EDITOR_SOURCES}
        COMMENT "Publishing WinRaycastEditor (self-contained Win-x64) for packaging"
        VERBATIM)

    add_custom_target(WinRaycastEditorPublish ALL
        DEPENDS "${WINRAYCAST_EDITOR_PUBLISH_STAMP}")

    add_custom_target(WinRaycastEditorPublishFresh
        COMMAND "${CMAKE_COMMAND}" -E rm -rf "${WINRAYCAST_EDITOR_PUBLISH_DIR}"
        COMMAND "${DOTNET_EXECUTABLE}" publish "${WINRAYCAST_EDITOR_PROJECT}"
            -c Release
            -r win-x64
            --self-contained true
            -p:DebugType=None
            -p:DebugSymbols=false
            -o "${WINRAYCAST_EDITOR_PUBLISH_DIR}"
        COMMAND "${CMAKE_COMMAND}" -E touch "${WINRAYCAST_EDITOR_PUBLISH_STAMP}"
        DEPENDS ${WINRAYCAST_EDITOR_SOURCES}
        COMMENT "Fresh-publishing WinRaycastEditor for installer packaging"
        VERBATIM)

    add_dependencies(WinRaycastEditorPublishFresh WinRaycastEditorPublish)

    set_target_properties(WinRaycastEditorPublishFresh PROPERTIES
        FOLDER "Packaging"
        EXCLUDE_FROM_DEFAULT_BUILD TRUE)

    # Surface the editor C# projects inside the Visual Studio solution so the
    # whole workspace (engine + editor + installer) lives in WinRayCast.sln.
    if(CMAKE_GENERATOR MATCHES "Visual Studio")
        set(WINRAYCAST_NETSDK_PROJECT_GUID
            "9A19103F-16F7-4668-BE54-9A1E7A4F7556")

        include_external_msproject(WinRaycastEditor.Core
            "${CMAKE_CURRENT_SOURCE_DIR}/tools/WinRaycastEditor/src/WinRaycastEditor.Core/WinRaycastEditor.Core.csproj"
            TYPE ${WINRAYCAST_NETSDK_PROJECT_GUID}
            PLATFORM "Any CPU")

        include_external_msproject(WinRaycastEditor
            "${CMAKE_CURRENT_SOURCE_DIR}/tools/WinRaycastEditor/src/WinRaycastEditor/WinRaycastEditor.csproj"
            TYPE ${WINRAYCAST_NETSDK_PROJECT_GUID}
            PLATFORM "Any CPU"
            WinRaycastEditor.Core)

        if(BUILD_TESTING)
            include_external_msproject(WinRaycastEditor.Tests
                "${CMAKE_CURRENT_SOURCE_DIR}/tools/WinRaycastEditor/src/WinRaycastEditor.Tests/WinRaycastEditor.Tests.csproj"
                TYPE ${WINRAYCAST_NETSDK_PROJECT_GUID}
                PLATFORM "Any CPU"
                WinRaycastEditor.Core
                WinRaycastEditor)
        endif()

        set_target_properties(WinRaycastEditor.Core PROPERTIES
            FOLDER "Editor"
            EXCLUDE_FROM_ALL TRUE
            EXCLUDE_FROM_DEFAULT_BUILD TRUE)
        set_target_properties(WinRaycastEditor PROPERTIES
            FOLDER "Editor"
            EXCLUDE_FROM_ALL TRUE
            EXCLUDE_FROM_DEFAULT_BUILD TRUE)
        if(BUILD_TESTING)
            set_target_properties(WinRaycastEditor.Tests PROPERTIES
                FOLDER "Editor"
                EXCLUDE_FROM_ALL TRUE
                EXCLUDE_FROM_DEFAULT_BUILD TRUE)
        endif()
    endif()
else()
    message(WARNING "dotnet not found in PATH — editor will be skipped during packaging.")
    add_custom_target(WinRaycastEditorPublish
        COMMAND "${CMAKE_COMMAND}" -E echo
            "Skipping editor publish: dotnet not found. Install .NET 8 SDK to package the editor.")
endif()

###############################################################################
# Install rules — used by the standalone installer (CPack/NSIS)
###############################################################################

install(TARGETS WinRayCastPlayer
    RUNTIME DESTINATION .
    COMPONENT engine)

if(WINRAYCAST_SHERPA_ONNX_DLLS)
    install(FILES ${WINRAYCAST_SHERPA_ONNX_DLLS}
        DESTINATION .
        COMPONENT engine)
endif()
if(WINRAYCAST_TTS_MODEL_DIR)
    install(DIRECTORY "${WINRAYCAST_TTS_MODEL_DIR}/"
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
        if(NOT EXISTS \"${WINRAYCAST_EDITOR_EXE}\")
            message(FATAL_ERROR
                \"WinRaycastEditor.exe not found at ${WINRAYCAST_EDITOR_EXE}. \"
                \"Build the WinRaycastEditorPublish target before running cpack.\")
        endif()
    " COMPONENT editor)

    install(DIRECTORY "${WINRAYCAST_EDITOR_PUBLISH_DIR}/"
        DESTINATION editor
        COMPONENT editor
        USE_SOURCE_PERMISSIONS
        PATTERN "*.pdb" EXCLUDE)
endif()

###############################################################################
# CPack — NSIS installer
###############################################################################

set(CPACK_PACKAGE_NAME              "WinRayCast")
set(CPACK_PACKAGE_VENDOR            "Antonino Calderone")
set(CPACK_PACKAGE_CONTACT           "antonino.calderone@gmail.com")
set(CPACK_PACKAGE_DESCRIPTION_SUMMARY
    "WinRayCast — classic software ray-casting engine, editor, and demo world")
set(CPACK_PACKAGE_VERSION           "${PROJECT_VERSION}")
set(CPACK_PACKAGE_INSTALL_DIRECTORY "WinRayCast")
set(CPACK_RESOURCE_FILE_LICENSE     "${CMAKE_CURRENT_SOURCE_DIR}/COPYING")
set(CPACK_PACKAGE_FILE_NAME
    "WinRayCast-${PROJECT_VERSION}-win-x64-setup")

if(NOT CPACK_GENERATOR)
    set(CPACK_GENERATOR "NSIS;ZIP")
endif()
set(CPACK_NSIS_DISPLAY_NAME         "WinRayCast ${PROJECT_VERSION}")
set(CPACK_NSIS_PACKAGE_NAME         "WinRayCast ${PROJECT_VERSION}")
set(CPACK_NSIS_HELP_LINK            "https://github.com/eantcal/winraycast")
set(CPACK_NSIS_URL_INFO_ABOUT       "https://github.com/eantcal/winraycast")
set(CPACK_NSIS_CONTACT              "${CPACK_PACKAGE_CONTACT}")
set(CPACK_NSIS_MODIFY_PATH          OFF)
set(CPACK_NSIS_ENABLE_UNINSTALL_BEFORE_INSTALL ON)
set(CPACK_NSIS_MUI_ICON
    "${CMAKE_CURRENT_SOURCE_DIR}/tools/WinRaycastEditor/src/WinRaycastEditor/Assets/AppIcon.ico")
set(CPACK_NSIS_MUI_UNIICON
    "${CMAKE_CURRENT_SOURCE_DIR}/tools/WinRaycastEditor/src/WinRaycastEditor/Assets/AppIcon.ico")

set(CPACK_NSIS_MUI_FINISHPAGE_RUN "WinRayCastPlayer.exe")

set(CPACK_NSIS_CREATE_ICONS_EXTRA
    "CreateShortCut '$SMPROGRAMS\\\\$STARTMENU_FOLDER\\\\WinRayCast Player (demo world).lnk' '$INSTDIR\\\\WinRayCastPlayer.exe' '$\\\"$INSTDIR\\\\res\\\\worlds\\\\demo_embedded\\\\demo.world.json$\\\"' '$INSTDIR\\\\WinRayCastPlayer.exe' 0 SW_SHOWNORMAL '' 'Run the self-contained demo world'
    CreateShortCut '$SMPROGRAMS\\\\$STARTMENU_FOLDER\\\\WinRayCast Player.lnk' '$INSTDIR\\\\WinRayCastPlayer.exe' '' '$INSTDIR\\\\WinRayCastPlayer.exe' 0 SW_SHOWNORMAL '' 'Run the default WinRayCast world'
    CreateShortCut '$SMPROGRAMS\\\\$STARTMENU_FOLDER\\\\WinRaycast Editor.lnk' '$INSTDIR\\\\editor\\\\WinRaycastEditor.exe' '' '$INSTDIR\\\\editor\\\\WinRaycastEditor.exe' 0 SW_SHOWNORMAL '' 'Author worlds, blocks, and sprites for WinRayCast'")
set(CPACK_NSIS_DELETE_ICONS_EXTRA
    "Delete '$SMPROGRAMS\\\\$STARTMENU_FOLDER\\\\WinRayCast Player (demo world).lnk'
    Delete '$SMPROGRAMS\\\\$STARTMENU_FOLDER\\\\WinRayCast Player.lnk'
    Delete '$SMPROGRAMS\\\\$STARTMENU_FOLDER\\\\WinRaycast Editor.lnk'")

set(CPACK_COMPONENTS_ALL engine)
if(DOTNET_EXECUTABLE)
    list(APPEND CPACK_COMPONENTS_ALL editor)
endif()

set(CPACK_COMPONENT_ENGINE_DISPLAY_NAME "Engine and demo world")
set(CPACK_COMPONENT_ENGINE_DESCRIPTION
    "WinRayCastPlayer.exe and self-contained world packages with maps, textures, and sprites.")
set(CPACK_COMPONENT_ENGINE_REQUIRED ON)

set(CPACK_COMPONENT_EDITOR_DISPLAY_NAME "WinRaycast Editor")
set(CPACK_COMPONENT_EDITOR_DESCRIPTION
    "Self-contained .NET 8 WPF authoring tool for maps, blocks, and sprites.")

include(CPack)

set(WINRAYCAST_PACKAGE_DEPENDENCIES
    WinRayCastPlayer
    WinRayCastPackageResources)
if(DOTNET_EXECUTABLE)
    list(APPEND WINRAYCAST_PACKAGE_DEPENDENCIES WinRaycastEditorPublishFresh)
endif()

foreach(WINRAYCAST_CPACK_TARGET package PACKAGE)
    if(TARGET ${WINRAYCAST_CPACK_TARGET})
        add_dependencies(
            ${WINRAYCAST_CPACK_TARGET}
            ${WINRAYCAST_PACKAGE_DEPENDENCIES})
    endif()
endforeach()

add_custom_target(WinRayCastFreshPackage
    COMMAND "${CMAKE_COMMAND}" -E rm -rf "${CMAKE_BINARY_DIR}/_CPack_Packages"
    COMMAND "${CMAKE_CPACK_COMMAND}"
        -C "$<CONFIG>"
        --config "${CMAKE_BINARY_DIR}/CPackConfig.cmake"
    DEPENDS ${WINRAYCAST_PACKAGE_DEPENDENCIES} ${WINRAYCAST_RESOURCE_FILES}
    COMMENT "Creating a fresh WinRayCast installer/package from rebuilt binaries and current resources"
    VERBATIM)

set_target_properties(WinRayCastFreshPackage PROPERTIES FOLDER "Packaging")

if(BUILD_TESTING)
    set(gtest_force_shared_crt ON CACHE BOOL "" FORCE)
    set(INSTALL_GTEST OFF CACHE BOOL "" FORCE)
    set(INSTALL_GMOCK OFF CACHE BOOL "" FORCE)
    FetchContent_Declare(
        googletest
        URL https://github.com/google/googletest/archive/refs/tags/v1.14.0.zip
        DOWNLOAD_EXTRACT_TIMESTAMP TRUE)
    FetchContent_MakeAvailable(googletest)

    add_executable(WinRayCastTests
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

    target_include_directories(WinRayCastTests PRIVATE
        "${CMAKE_CURRENT_SOURCE_DIR}/src/engine"
        "${CMAKE_CURRENT_SOURCE_DIR}/src/win"
        "${CMAKE_CURRENT_SOURCE_DIR}/src/app")

    target_compile_definitions(WinRayCastTests PRIVATE
        WINRAYCAST_REPO_DIR="${CMAKE_CURRENT_SOURCE_DIR}")

    target_link_libraries(WinRayCastTests PRIVATE
        WinRayCastEngine
        GTest::gtest_main)

    include(GoogleTest)
    gtest_discover_tests(WinRayCastTests
        DISCOVERY_MODE PRE_TEST)

    set_target_properties(WinRayCastTests PROPERTIES FOLDER "Engine/Tests")

    # Offscreen rendering benchmark (standalone console app, not a ctest).
    add_executable(WinRayCastBenchmark
        tests/RenderBenchmarkMain.cpp
        tests/BenchmarkScene.h)

    target_include_directories(WinRayCastBenchmark PRIVATE
        "${CMAKE_CURRENT_SOURCE_DIR}/src/engine")

    target_link_libraries(WinRayCastBenchmark PRIVATE WinRayCastEngine)
    set_target_properties(WinRayCastBenchmark PROPERTIES FOLDER "Engine/Tests")

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
