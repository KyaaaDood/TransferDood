@echo off
cd /d "%~dp0"
echo ==== TransferDood Android %DATE% %TIME% ==== > build-log.txt
echo Carpeta: %CD% >> build-log.txt
echo Carpeta: %CD%

if exist local.properties goto sdkok
if defined ANDROID_HOME goto sdkok
if defined ANDROID_SDK_ROOT goto sdkok
if not exist "%LOCALAPPDATA%\Android\Sdk" goto nosdk
set "SDKPATH=%LOCALAPPDATA%\Android\Sdk"
call set "SDKPATH=%%SDKPATH:\=/%%"
call echo sdk.dir=%%SDKPATH%%> local.properties
echo Se creo local.properties
goto sdkok
:nosdk
echo ERROR: No se encontro el SDK de Android.
echo ERROR: No se encontro el SDK de Android. >> build-log.txt
echo Abre esta carpeta con Android Studio una vez y vuelve a ejecutar este script.
goto fin
:sdkok

rem --- Java: Gradle 8.9 necesita JDK 17 a 21 (NO funciona con Java 25 ni con Java 8) ---
set "JAVA_HOME="
for /d %%D in ("%USERPROFILE%\.jdks\*-21*" "%USERPROFILE%\.jdks\*-17*" "%ProgramFiles%\Eclipse Adoptium\*-21*" "%ProgramFiles%\Eclipse Adoptium\*-17*" "%ProgramFiles%\Java\*-21*" "%ProgramFiles%\Java\*-17*" "%ProgramFiles%\Microsoft\*-21*" "%ProgramFiles%\Microsoft\*-17*" "%ProgramFiles%\Amazon Corretto\*-21*" "%ProgramFiles%\Zulu\*-21*") do if exist "%%~D\bin\java.exe" if not defined JAVA_HOME set "JAVA_HOME=%%~D"
if defined JAVA_HOME goto javaok
if exist "%ProgramFiles%\Android\Android Studio\jbr\bin\java.exe" set "JAVA_HOME=%ProgramFiles%\Android\Android Studio\jbr"
:javaok
echo JAVA_HOME=%JAVA_HOME% >> build-log.txt
echo JAVA_HOME=%JAVA_HOME%
if not defined JAVA_HOME goto nojava
"%JAVA_HOME%\bin\java.exe" -version >> build-log.txt 2>&1
"%JAVA_HOME%\bin\java.exe" -version
"%JAVA_HOME%\bin\java.exe" -version 2>&1 | findstr /r /c:"version .2[2-9]" >nul
if not errorlevel 1 goto nojava
set "PATH=%JAVA_HOME%\bin;%PATH%"
goto compilar

:nojava
echo.
echo ERROR: No hay un JDK compatible. Gradle necesita JDK 17 a 21 y el de Android Studio es mas nuevo.
echo ERROR: No hay JDK 17-21 compatible. >> build-log.txt
echo.
echo SOLUCION: en Android Studio abre Settings - Build Tools - Gradle - Gradle JDK,
echo elige "Download JDK..." y descarga la version 21 ^(se guarda en %USERPROFILE%\.jdks^).
echo O instala Temurin 21 desde https://adoptium.net  y vuelve a ejecutar este script.
goto fin

:compilar
echo.
echo Compilando, puede tardar varios minutos la primera vez...
call gradlew.bat assembleRelease --console=plain "-Dorg.gradle.java.home=%JAVA_HOME%" >> build-log.txt 2>&1
if errorlevel 1 goto fallo
copy /y app\build\outputs\apk\release\app-release.apk TransferDood.apk >nul
echo.
echo ===== LISTO: TransferDood.apk =====
goto fin

:fallo
echo.
echo ===== LA COMPILACION FALLO =====
echo Ultimas lineas del registro:
powershell -NoProfile -Command "Get-Content build-log.txt -Tail 40"
echo.
echo El registro completo esta en: %~dp0build-log.txt

:fin
echo.
pause
