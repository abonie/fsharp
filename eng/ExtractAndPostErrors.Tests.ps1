[CmdletBinding()]
param (
    [Parameter(Mandatory = $false)]
    [string]$TestDataPath = "$PSScriptRoot\TestData",
    
    [Parameter(Mandatory = $false)]
    [switch]$MockGitHubApi,
    
    [Parameter(Mandatory = $false)]
    [string]$GitHubToken
)

# Import required modules for mocking if needed
if ($MockGitHubApi) {
    # Use Pester for mocking if available
    if (-not (Get-Module -ListAvailable -Name Pester)) {
        Write-Host "Pester module not found. Installing..."
        Install-Module -Name Pester -Force -SkipPublisherCheck
    }
    Import-Module Pester
}

# Setup test environment
$TestLogsPath = Join-Path $TestDataPath "logs"
$TestTestResultsPath = Join-Path $TestDataPath "TestResults"

# Create test directories if they don't exist
if (-not (Test-Path $TestLogsPath)) {
    New-Item -ItemType Directory -Path $TestLogsPath -Force | Out-Null
}

if (-not (Test-Path $TestTestResultsPath)) {
    New-Item -ItemType Directory -Path $TestTestResultsPath -Force | Out-Null
}

# Test helper functions
function Generate-MockCompilationErrors {
    param (
        [string]$OutputPath
    )
    
    Write-Host "Generating mock compilation errors in $OutputPath"
    
    # Create a sample binlog file (just text for testing)
    $binlogContent = @"
Build started 2023-10-10 12:00:00.
error FS0001: This expression was expected to have type 'int' but here has type 'string'
   at line 10 in file Program.fs
error CS0029: Cannot implicitly convert type 'string' to 'int'
   at line 15 in file Program.cs
"@
    
    Set-Content -Path (Join-Path $OutputPath "build.binlog") -Value $binlogContent
    
    # Create a sample log file
    $logContent = @"
[12:00:00] Building project...
[12:01:00] error FS0019: The type 'string' is not compatible with the type 'int'
   at line 25 in file DataAccess.fs
[12:02:00] Exception: System.NullReferenceException: Object reference not set to an instance of an object.
   at MyNamespace.MyClass.MyMethod() in MyFile.fs:line 42
"@
    
    Set-Content -Path (Join-Path $OutputPath "build.log") -Value $logContent
}

function Generate-MockTestResults {
    param (
        [string]$OutputPath
    )
    
    Write-Host "Generating mock test results in $OutputPath"
    
    # Create a sample NUnit test result file
    $nunitContent = @"
<?xml version="1.0" encoding="utf-8"?>
<test-run id="2" testcasecount="3" result="Failed" total="3" passed="1" failed="2">
  <test-suite id="1" name="TestSuite" fullname="MyProject.TestSuite" testcasecount="3" result="Failed">
    <test-case id="1" name="PassingTest" fullname="MyProject.TestSuite.PassingTest" result="Passed" />
    <test-case id="2" name="FailingTest1" fullname="MyProject.TestSuite.FailingTest1" result="Failed">
      <failure>
        <message>Expected True but got False</message>
        <stack-trace>at MyProject.TestSuite.FailingTest1() in TestFile.fs:line 25</stack-trace>
      </failure>
    </test-case>
    <test-case id="3" name="FailingTest2" fullname="MyProject.TestSuite.FailingTest2" result="Failed">
      <failure>
        <message>Values differ. Expected:10 Actual:5</message>
        <stack-trace>at MyProject.TestSuite.FailingTest2() in TestFile.fs:line 36</stack-trace>
      </failure>
    </test-case>
  </test-suite>
</test-run>
"@
    
    Set-Content -Path (Join-Path $OutputPath "nunit-results.xml") -Value $nunitContent
    
    # Create a sample xUnit test result file
    $xunitContent = @"
<?xml version="1.0" encoding="utf-8"?>
<assemblies>
  <assembly name="MyTests.dll" environment="64-bit .NET 6.0" test-framework="xUnit.net" run-date="2023-10-10" run-time="12:15:00">
    <collection name="Test collection 1">
      <test name="MyTests.UnitTest1.Test1" result="Pass" />
      <test name="MyTests.UnitTest1.Test2" result="Fail">
        <failure>
          <message>Assert.Equal() Failure: Expected: 42, Actual: 24</message>
          <stack-trace>at MyTests.UnitTest1.Test2() in UnitTest1.fs:line 55</stack-trace>
        </failure>
      </test>
    </collection>
  </assembly>
</assemblies>
"@
    
    Set-Content -Path (Join-Path $OutputPath "xunit-results.xml") -Value $xunitContent
}

function Mock-GitHubAPI {
    Write-Host "Setting up GitHub API mocking"
    
    # Replace the external GitHub API functions with mocks
    function global:Post-GitHubComment {
        param (
            [string]$Owner,
            [string]$Repo,
            [string]$PrNumber,
            [string]$CommentBody,
            [string]$Token
        )
        
        Write-Host "MOCK: Posting GitHub comment to PR #$PrNumber"
        Write-Host "Comment body:"
        Write-Host "-------------"
        Write-Host $CommentBody
        Write-Host "-------------"
        
        # Return mock response
        return @{
            id = 12345
            html_url = "https://github.com/$Owner/$Repo/pull/$PrNumber#issuecomment-12345"
        }
    }
    
    function global:Update-ExistingComment {
        param (
            [string]$Owner,
            [string]$Repo,
            [string]$PrNumber,
            [string]$CommentId,
            [string]$CommentBody,
            [string]$Token
        )
        
        Write-Host "MOCK: Updating GitHub comment #$CommentId on PR #$PrNumber"
        Write-Host "Comment body:"
        Write-Host "-------------"
        Write-Host $CommentBody
        Write-Host "-------------"
        
        # Return mock response
        return @{
            id = $CommentId
            html_url = "https://github.com/$Owner/$Repo/pull/$PrNumber#issuecomment-$CommentId"
        }
    }
    
    function global:Find-ExistingComment {
        param (
            [string]$Owner,
            [string]$Repo,
            [string]$PrNumber,
            [string]$CommentPrefix,
            [string]$Token
        )
        
        Write-Host "MOCK: Finding existing comments on PR #$PrNumber"
        Write-Host "Looking for prefix: $CommentPrefix"
        
        # Randomly decide whether to return an existing comment or not
        $random = Get-Random -Minimum 0 -Maximum 2
        if ($random -eq 0) {
            Write-Host "MOCK: Found existing comment"
            return 67890
        } else {
            Write-Host "MOCK: No existing comment found"
            return $null
        }
    }
    
    # Export the functions to make them available to the script being tested
    Export-ModuleMember -Function Post-GitHubComment, Update-ExistingComment, Find-ExistingComment
}

# Setup mock environment variables
$env:BUILD_BUILDID = "12345"
$env:AGENT_JOBNAME = "TestJob"
$env:BUILD_SOURCESDIRECTORY = $TestDataPath
$env:SYSTEM_PULLREQUEST_PULLREQUESTNUMBER = "123"
$env:BUILD_REPOSITORY_NAME = "testowner/testrepo"
$env:_BUILDCONFIG = "Release"
$env:SYSTEM_TEAMFOUNDATIONSERVERURI = "https://dev.azure.com/testorg/"
$env:SYSTEM_TEAMPROJECT = "testproject"
$env:SYSTEM_COLLECTIONURI = "https://dev.azure.com/testorg/"
$env:SYSTEM_DEFINITIONID = "789"
$env:BUILD_DEFINITIONNAME = "CI-Pipeline"

# Generate test data
Write-Host "Generating test data..."
$ReleasePath = Join-Path $TestLogsPath "Release"
$ReleaseTestResultsPath = Join-Path $TestTestResultsPath "Release"

# Create directories
New-Item -ItemType Directory -Path $ReleasePath -Force | Out-Null
New-Item -ItemType Directory -Path $ReleaseTestResultsPath -Force | Out-Null

# Generate mock data
Generate-MockCompilationErrors -OutputPath $ReleasePath
Generate-MockTestResults -OutputPath $ReleaseTestResultsPath

# Set up mocking if requested
if ($MockGitHubApi) {
    Mock-GitHubAPI
    # Don't use real GitHub token in mock mode
    $env:GITHUB_PAT = "MOCK_TOKEN_FOR_TESTING"
} else {
    # Use the provided token for real API calls
    $env:GITHUB_PAT = $GitHubToken
}

Write-Host "Running ExtractAndPostErrors.ps1..."

# Run the script being tested
$scriptPath = Join-Path $PSScriptRoot ".." "ExtractAndPostErrors.ps1"
& $scriptPath

# Clean up after test
Write-Host "Cleaning up test environment variables..."
Remove-Item Env:\BUILD_BUILDID
Remove-Item Env:\AGENT_JOBNAME
Remove-Item Env:\BUILD_SOURCESDIRECTORY
Remove-Item Env:\SYSTEM_PULLREQUEST_PULLREQUESTNUMBER
Remove-Item Env:\BUILD_REPOSITORY_NAME
Remove-Item Env:\GITHUB_PAT
Remove-Item Env:\_BUILDCONFIG
Remove-Item Env:\SYSTEM_TEAMFOUNDATIONSERVERURI
Remove-Item Env:\SYSTEM_TEAMPROJECT
Remove-Item Env:\SYSTEM_COLLECTIONURI
Remove-Item Env:\SYSTEM_DEFINITIONID
Remove-Item Env:\BUILD_DEFINITIONNAME

Write-Host "Test completed."
