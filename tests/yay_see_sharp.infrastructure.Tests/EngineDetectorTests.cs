using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Platform;

namespace yay_see_sharp.infrastructure.Tests;

public class EngineDetectorTests
{
    private static string CreateExecutableFile(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, "#!/bin/sh\n");
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }

    private static string CreateNonExecutableFile(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, "#!/bin/sh\n");
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return path;
    }

    [Test]
    public async Task Detects_yay_when_it_is_executable_on_path()
    {
        var dir = Path.Combine(Path.GetTempPath(), "engine-detector-test-" + Guid.NewGuid().ToString("N"));
        CreateExecutableFile(dir, "yay");

        var detector = new EngineDetector(dir);

        await Assert.That(detector.Detect()).IsEqualTo(PackageManagerEngine.Yay);
    }

    [Test]
    public async Task Does_not_detect_yay_when_the_file_exists_but_is_not_executable()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new TUnit.Core.Exceptions.SkipTestException("Executable-bit checks are only meaningful on Linux.");
        }

        var dir = Path.Combine(Path.GetTempPath(), "engine-detector-test-" + Guid.NewGuid().ToString("N"));
        CreateNonExecutableFile(dir, "yay");

        var detector = new EngineDetector(dir);

        await Assert.That(detector.Detect()).IsNull();
    }

    [Test]
    public async Task Falls_back_to_paru_when_yay_is_not_on_path()
    {
        var dir = Path.Combine(Path.GetTempPath(), "engine-detector-test-" + Guid.NewGuid().ToString("N"));
        CreateExecutableFile(dir, "paru");

        var detector = new EngineDetector(dir);

        await Assert.That(detector.Detect()).IsEqualTo(PackageManagerEngine.Paru);
    }

    [Test]
    public async Task Returns_null_when_neither_yay_nor_paru_are_on_path()
    {
        var dir = Path.Combine(Path.GetTempPath(), "engine-detector-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var detector = new EngineDetector(dir);

        await Assert.That(detector.Detect()).IsNull();
    }

    [Test]
    public async Task Returns_null_when_path_is_empty()
    {
        var detector = new EngineDetector(string.Empty);

        await Assert.That(detector.Detect()).IsNull();
    }
}
