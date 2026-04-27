# Install script for directory: C:/Users/ursae/Desktop/Git/SOEM

# Set the install prefix
if(NOT DEFINED CMAKE_INSTALL_PREFIX)
  set(CMAKE_INSTALL_PREFIX "C:/Users/ursae/Desktop/Git/SOEM/out/install/x64-Debug")
endif()
string(REGEX REPLACE "/$" "" CMAKE_INSTALL_PREFIX "${CMAKE_INSTALL_PREFIX}")

# Set the install configuration name.
if(NOT DEFINED CMAKE_INSTALL_CONFIG_NAME)
  if(BUILD_TYPE)
    string(REGEX REPLACE "^[^A-Za-z0-9_]+" ""
           CMAKE_INSTALL_CONFIG_NAME "${BUILD_TYPE}")
  else()
    set(CMAKE_INSTALL_CONFIG_NAME "Debug")
  endif()
  message(STATUS "Install configuration: \"${CMAKE_INSTALL_CONFIG_NAME}\"")
endif()

# Set the component getting installed.
if(NOT CMAKE_INSTALL_COMPONENT)
  if(COMPONENT)
    message(STATUS "Install component: \"${COMPONENT}\"")
    set(CMAKE_INSTALL_COMPONENT "${COMPONENT}")
  else()
    set(CMAKE_INSTALL_COMPONENT)
  endif()
endif()

# Is this installation the result of a crosscompile?
if(NOT DEFINED CMAKE_CROSSCOMPILING)
  set(CMAKE_CROSSCOMPILING "FALSE")
endif()

if(CMAKE_INSTALL_COMPONENT STREQUAL "Unspecified" OR NOT CMAKE_INSTALL_COMPONENT)
  file(INSTALL DESTINATION "${CMAKE_INSTALL_PREFIX}/lib" TYPE STATIC_LIBRARY FILES "C:/Users/ursae/Desktop/Git/SOEM/out/build/x64-Debug/soem.lib")
endif()

if(CMAKE_INSTALL_COMPONENT STREQUAL "Unspecified" OR NOT CMAKE_INSTALL_COMPONENT)
  include("C:/Users/ursae/Desktop/Git/SOEM/out/build/x64-Debug/CMakeFiles/soem.dir/install-cxx-module-bmi-Debug.cmake" OPTIONAL)
endif()

if(CMAKE_INSTALL_COMPONENT STREQUAL "Unspecified" OR NOT CMAKE_INSTALL_COMPONENT)
  if(EXISTS "$ENV{DESTDIR}${CMAKE_INSTALL_PREFIX}/share/soem/cmake/soemConfig.cmake")
    file(DIFFERENT _cmake_export_file_changed FILES
         "$ENV{DESTDIR}${CMAKE_INSTALL_PREFIX}/share/soem/cmake/soemConfig.cmake"
         "C:/Users/ursae/Desktop/Git/SOEM/out/build/x64-Debug/CMakeFiles/Export/39806c66e6e7fd9076eb39407f12ee6f/soemConfig.cmake")
    if(_cmake_export_file_changed)
      file(GLOB _cmake_old_config_files "$ENV{DESTDIR}${CMAKE_INSTALL_PREFIX}/share/soem/cmake/soemConfig-*.cmake")
      if(_cmake_old_config_files)
        string(REPLACE ";" ", " _cmake_old_config_files_text "${_cmake_old_config_files}")
        message(STATUS "Old export file \"$ENV{DESTDIR}${CMAKE_INSTALL_PREFIX}/share/soem/cmake/soemConfig.cmake\" will be replaced.  Removing files [${_cmake_old_config_files_text}].")
        unset(_cmake_old_config_files_text)
        file(REMOVE ${_cmake_old_config_files})
      endif()
      unset(_cmake_old_config_files)
    endif()
    unset(_cmake_export_file_changed)
  endif()
  file(INSTALL DESTINATION "${CMAKE_INSTALL_PREFIX}/share/soem/cmake" TYPE FILE FILES "C:/Users/ursae/Desktop/Git/SOEM/out/build/x64-Debug/CMakeFiles/Export/39806c66e6e7fd9076eb39407f12ee6f/soemConfig.cmake")
  if(CMAKE_INSTALL_CONFIG_NAME MATCHES "^([Dd][Ee][Bb][Uu][Gg])$")
    file(INSTALL DESTINATION "${CMAKE_INSTALL_PREFIX}/share/soem/cmake" TYPE FILE FILES "C:/Users/ursae/Desktop/Git/SOEM/out/build/x64-Debug/CMakeFiles/Export/39806c66e6e7fd9076eb39407f12ee6f/soemConfig-debug.cmake")
  endif()
endif()

if(CMAKE_INSTALL_COMPONENT STREQUAL "Unspecified" OR NOT CMAKE_INSTALL_COMPONENT)
  file(INSTALL DESTINATION "${CMAKE_INSTALL_PREFIX}/include/soem" TYPE FILE FILES
    "C:/Users/ursae/Desktop/Git/SOEM/soem/ethercat.h"
    "C:/Users/ursae/Desktop/Git/SOEM/soem/ethercatbase.h"
    "C:/Users/ursae/Desktop/Git/SOEM/soem/ethercatcoe.h"
    "C:/Users/ursae/Desktop/Git/SOEM/soem/ethercatconfig.h"
    "C:/Users/ursae/Desktop/Git/SOEM/soem/ethercatconfiglist.h"
    "C:/Users/ursae/Desktop/Git/SOEM/soem/ethercatdc.h"
    "C:/Users/ursae/Desktop/Git/SOEM/soem/ethercateoe.h"
    "C:/Users/ursae/Desktop/Git/SOEM/soem/ethercatfoe.h"
    "C:/Users/ursae/Desktop/Git/SOEM/soem/ethercatmain.h"
    "C:/Users/ursae/Desktop/Git/SOEM/soem/ethercatprint.h"
    "C:/Users/ursae/Desktop/Git/SOEM/soem/ethercatsoe.h"
    "C:/Users/ursae/Desktop/Git/SOEM/soem/ethercattype.h"
    "C:/Users/ursae/Desktop/Git/SOEM/osal/osal.h"
    "C:/Users/ursae/Desktop/Git/SOEM/osal/win32/osal_defs.h"
    "C:/Users/ursae/Desktop/Git/SOEM/osal/win32/osal_win32.h"
    "C:/Users/ursae/Desktop/Git/SOEM/oshw/win32/nicdrv.h"
    "C:/Users/ursae/Desktop/Git/SOEM/oshw/win32/oshw.h"
    )
endif()

if(NOT CMAKE_INSTALL_LOCAL_ONLY)
  # Include the install script for each subdirectory.
  include("C:/Users/ursae/Desktop/Git/SOEM/out/build/x64-Debug/test/simple_ng/cmake_install.cmake")
  include("C:/Users/ursae/Desktop/Git/SOEM/out/build/x64-Debug/test/linux/slaveinfo/cmake_install.cmake")
  include("C:/Users/ursae/Desktop/Git/SOEM/out/build/x64-Debug/test/linux/eepromtool/cmake_install.cmake")
  include("C:/Users/ursae/Desktop/Git/SOEM/out/build/x64-Debug/test/linux/simple_test/cmake_install.cmake")

endif()

if(CMAKE_INSTALL_COMPONENT)
  if(CMAKE_INSTALL_COMPONENT MATCHES "^[a-zA-Z0-9_.+-]+$")
    set(CMAKE_INSTALL_MANIFEST "install_manifest_${CMAKE_INSTALL_COMPONENT}.txt")
  else()
    string(MD5 CMAKE_INST_COMP_HASH "${CMAKE_INSTALL_COMPONENT}")
    set(CMAKE_INSTALL_MANIFEST "install_manifest_${CMAKE_INST_COMP_HASH}.txt")
    unset(CMAKE_INST_COMP_HASH)
  endif()
else()
  set(CMAKE_INSTALL_MANIFEST "install_manifest.txt")
endif()

if(NOT CMAKE_INSTALL_LOCAL_ONLY)
  string(REPLACE ";" "\n" CMAKE_INSTALL_MANIFEST_CONTENT
       "${CMAKE_INSTALL_MANIFEST_FILES}")
  file(WRITE "C:/Users/ursae/Desktop/Git/SOEM/out/build/x64-Debug/${CMAKE_INSTALL_MANIFEST}"
     "${CMAKE_INSTALL_MANIFEST_CONTENT}")
endif()
