cmake_minimum_required(VERSION 3.24)
project(WiseShellCpp VERSION 1.0.0 LANGUAGES C CXX)
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_AUTOMOC ON)
set(CMAKE_AUTORCC ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")
if(MSVC)
  add_compile_options(/utf-8 /W4)
  add_compile_definitions(NOMINMAX)
endif()
find_package(Qt6 6.2 REQUIRED COMPONENTS Core Gui Widgets Test Network)
include(CTest)
set(wiseshell_tests_enabled "${BUILD_TESTING}")
include(cmake/Dependencies.cmake)
set_target_properties(ssh PROPERTIES RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")
# Avoid windeployqt mistaking the third-party library for a Qt SDK module.
set_target_properties(qt6keychain PROPERTIES OUTPUT_NAME wiseshell-keychain)
add_library(wiseshell_core STATIC src/core/Workspace.cpp src/core/Credentials.cpp)
target_include_directories(wiseshell_core PUBLIC src)
target_include_directories(wiseshell_core PRIVATE "${qtkeychain_SOURCE_DIR}" "${qtkeychain_BINARY_DIR}")
target_link_libraries(wiseshell_core PUBLIC Qt6::Core Qt6::Gui qt6keychain)
add_library(wiseshell_transport STATIC src/transport/Connection.cpp src/transport/TerminalSession.cpp src/transport/SftpSession.cpp)
target_include_directories(wiseshell_transport PUBLIC "${libssh_SOURCE_DIR}/include" "${libssh_BINARY_DIR}/include")
target_link_libraries(wiseshell_transport PUBLIC wiseshell_core Qt6::Network ssh)
add_library(wiseshell_terminal STATIC src/terminal/TerminalWidget.cpp)
target_link_libraries(wiseshell_terminal PUBLIC wiseshell_core Qt6::Widgets vterm)
add_library(wiseshell_ui STATIC src/ui/MainWindow.cpp src/ui/SessionDialog.cpp src/ui/SftpWindow.cpp src/ui/RemoteDirectoryDialog.cpp)
target_link_libraries(wiseshell_ui PUBLIC wiseshell_transport wiseshell_terminal)
qt_add_executable(WiseShellCpp WIN32 MACOSX_BUNDLE src/main.cpp resources.qrc)
target_link_libraries(WiseShellCpp PRIVATE wiseshell_ui)
if(WIN32)
  configure_file(assets/app.rc.in "${CMAKE_CURRENT_BINARY_DIR}/app.rc" @ONLY)
  target_sources(WiseShellCpp PRIVATE "${CMAKE_CURRENT_BINARY_DIR}/app.rc")
endif()
set_target_properties(WiseShellCpp PROPERTIES MACOSX_BUNDLE_BUNDLE_NAME WiseShellCpp MACOSX_BUNDLE_GUI_IDENTIFIER io.wiseshell.cpp)
if(WIN32)
  # Keep the build-tree executable runnable when opened directly from Explorer.
  # The release packager performs the same deployment for distribution archives.
  add_custom_command(TARGET WiseShellCpp POST_BUILD
    COMMAND "$<TARGET_FILE_DIR:Qt6::qmake>/windeployqt.exe"
      --release --no-translations --no-compiler-runtime
      --dir "$<TARGET_FILE_DIR:WiseShellCpp>"
      "$<TARGET_FILE:WiseShellCpp>"
    VERBATIM)
  foreach(_runtime_name IN ITEMS libcrypto-3-x64.dll libssl-3-x64.dll zlib1.dll)
    foreach(_runtime_dir IN ITEMS "${OPENSSL_ROOT_DIR}/bin" "${ZLIB_ROOT}/bin")
      if(EXISTS "${_runtime_dir}/${_runtime_name}")
        add_custom_command(TARGET WiseShellCpp POST_BUILD
          COMMAND "${CMAKE_COMMAND}" -E copy_if_different
            "${_runtime_dir}/${_runtime_name}" "$<TARGET_FILE_DIR:WiseShellCpp>/${_runtime_name}"
          VERBATIM)
        break()
      endif()
    endforeach()
  endforeach()
endif()
if(BUILD_TESTING)
  add_executable(wiseshell_tests tests/CoreTests.cpp)
  target_link_libraries(wiseshell_tests PRIVATE wiseshell_terminal wiseshell_transport Qt6::Test)
  add_test(NAME core_terminal COMMAND wiseshell_tests)
  set_tests_properties(core_terminal PROPERTIES ENVIRONMENT "QT_QPA_PLATFORM=offscreen")
  add_executable(wiseshell_integration tests/IntegrationTests.cpp)
  target_link_libraries(wiseshell_integration PRIVATE wiseshell_transport Qt6::Test)
  add_test(NAME ssh_sftp COMMAND wiseshell_integration)
  set_tests_properties(ssh_sftp PROPERTIES SKIP_REGULAR_EXPRESSION "Set WISESHELL_TEST_SSH_PORT" TIMEOUT 180)
  add_executable(wiseshell_ui_tests tests/UiTests.cpp resources.qrc)
  target_link_libraries(wiseshell_ui_tests PRIVATE wiseshell_ui Qt6::Test)
  add_test(NAME ui_workflows COMMAND wiseshell_ui_tests)
  set_tests_properties(ui_workflows PROPERTIES ENVIRONMENT "QT_QPA_PLATFORM=offscreen")
  if(WIN32)
    # windeployqt includes the Windows platform plugin for the application, but the
    # automated GUI tests intentionally run with Qt's offscreen platform plugin.
    set(_wiseshell_offscreen_plugin "${Qt6_DIR}/../../../plugins/platforms/qoffscreen.dll")
    foreach(_test_target IN ITEMS wiseshell_tests wiseshell_ui_tests)
      add_custom_command(TARGET ${_test_target} POST_BUILD
        COMMAND "${CMAKE_COMMAND}" -E make_directory "$<TARGET_FILE_DIR:${_test_target}>/platforms"
        COMMAND "${CMAKE_COMMAND}" -E copy_if_different "${_wiseshell_offscreen_plugin}"
                "$<TARGET_FILE_DIR:${_test_target}>/platforms/qoffscreen.dll"
        VERBATIM)
    endforeach()
  endif()
endif()
install(TARGETS WiseShellCpp RUNTIME DESTINATION . BUNDLE DESTINATION .)
install(FILES THIRD_PARTY_NOTICES.md DESTINATION licenses)
set(CPACK_PACKAGE_NAME WiseShellCpp)
set(CPACK_GENERATOR ZIP)
include(CPack)
