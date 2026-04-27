# Ignore local modifications to SOEM CMake-generated Visual Studio project files.
# This is local Git index state. Run this once per clone.

git ls-files "SOEM/build/*" "SOEM/build/**/*" | ForEach-Object {
    git update-index --skip-worktree $_
}