#----------------------------------------------------------------
# Generated CMake target import file for configuration "Debug".
#----------------------------------------------------------------

# Commands may need to know the format version.
set(CMAKE_IMPORT_FILE_VERSION 1)

# Import target "soem" for configuration "Debug"
set_property(TARGET soem APPEND PROPERTY IMPORTED_CONFIGURATIONS DEBUG)
set_target_properties(soem PROPERTIES
  IMPORTED_LINK_INTERFACE_LANGUAGES_DEBUG "C"
  IMPORTED_LOCATION_DEBUG "${_IMPORT_PREFIX}/lib/soem.lib"
  )

list(APPEND _cmake_import_check_targets soem )
list(APPEND _cmake_import_check_files_for_soem "${_IMPORT_PREFIX}/lib/soem.lib" )

# Commands beyond this point should not need to know the version.
set(CMAKE_IMPORT_FILE_VERSION)
