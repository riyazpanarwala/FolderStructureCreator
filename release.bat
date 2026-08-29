@echo off
set /p VERSION="Enter version tag (e.g. v5.0.2): "
if "%VERSION%"=="" exit /b

echo %VERSION% | findstr /b /i "v" >nul
if errorlevel 1 set "VERSION=v%VERSION%"

echo.
echo ==> Creating tag %VERSION%...
git tag %VERSION%
if errorlevel 1 goto :error

echo ==> Pushing tag %VERSION% to origin...
git push origin %VERSION%
if errorlevel 1 goto :error

echo.
echo ==> Success! GitHub Actions is now building and releasing %VERSION%.
echo.
pause
exit /b

:error
echo.
echo [ERROR] Failed to tag or push release. Check error above.
echo.
pause
