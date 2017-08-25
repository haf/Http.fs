# encoding: UTF-8
require 'bundler/setup'
require 'albacore'
require 'albacore/tasks/release'
require 'albacore/tasks/versionizer'

Configuration = ENV['CONFIGURATION'] || 'Release'
HttpFsStrongName = ENV['HTTPFS_STRONG_NAME'] && true || false

Albacore::Tasks::Versionizer.new :versioning

desc 'create assembly infos'
asmver_files :assembly_info do |a|
  a.files = FileList['**/*proj'] # optional, will find all projects recursively by default

  a.attributes assembly_description: 'A simple, functional HTTP client library for F#',
               assembly_configuration: Configuration,
               assembly_company: 'None',
               assembly_copyright: 'Contributors',
               assembly_version: ENV['LONG_VERSION'],
               assembly_file_version: ENV['LONG_VERSION'],
               assembly_informational_version: ENV['BUILD_VERSION']
end

task :restore_dotnetcli do
  system "dotnet", %W|restore|
end  

desc 'Perform fast build (warn: doesn\'t d/l deps)'
task :quick_compile do
  system "dotnet", %W|build -c #{Configuration} #{HttpFsStrongName ? "/p:AssemblyStrongName=true" : ""} --no-restore|
end

task :paket_replace do
  sh %{ruby -pi.bak -e "gsub(/module YoLo/, 'module internal HttpFs.YoLo')" paket-files/haf/YoLo/YoLo.fs}
  sh %{ruby -pi.bak -e "gsub(/namespace Logary.Facade/, 'namespace HttpFs.Logging')" paket-files/logary/logary/src/Logary.Facade/Facade.fs}
end

task :paket_restore do
  system './.paket/paket.exe', 'restore', clr_command: true
end

desc 'restore all nuget packages files'
task :restore => [:paket_restore, :paket_replace, :restore_dotnetcli]

desc 'Perform full build'
task :compile => [:versioning, :restore, :assembly_info] do |b|
  # https://github.com/dotnet/sdk/issues/335
  # https://github.com/dotnet/netcorecli-fsc/wiki/.NET-Core-SDK-1.0#known-issues
  if ENV["TRAVIS_OS_NAME"] == "linux" then
    Kernel.system({"FrameworkPathOverride" => "#{ENV["MONO_BASE_PATH"]}/4.5/"},
                    "dotnet build HttpFs -c #{Configuration} --no-restore --framework net45") or exit(1)
    system "dotnet", %W|build HttpFs -c #{Configuration} --no-restore --framework netstandard2.0|
    Kernel.system({"FrameworkPathOverride" => "#{ENV["MONO_BASE_PATH"]}/4.5/"},
                    "dotnet build HttpFs.IntegrationTests -c #{Configuration} --no-restore --framework net461") or exit(1)
    system "dotnet", %W|build HttpFs.UnitTests -c #{Configuration} --no-restore --framework netcoreapp2.0|
    system "dotnet", %W|build HttpFs.IntegrationTests -c #{Configuration} --no-restore --framework netcoreapp2.0|
  else
    system "dotnet", %W|build -c #{Configuration} --no-restore|
  end
end

directory 'build/pkg'

desc 'package nugets'
task :create_nugets do
  system "dotnet", %W|pack Httpfs/Httpfs.fsproj --no-build --no-restore -c #{Configuration} -o ../build/pkg /p:Version=#{ENV['NUGET_VERSION']}|
end

namespace :tests do
  task :integration do
    system "dotnet", %W|run -p HttpFs.IntegrationTests -c #{Configuration} --no-restore --no-build --framework netcoreapp2.0|
    system "HttpFs.IntegrationTests/bin/#{Configuration}/net461/HttpFs.IntegrationTests.exe", clr_command: true
  end
  task :unit do
    system "dotnet", %W|run -p HttpFs.UnitTests -c #{Configuration} --no-restore --no-build|
  end
end

task :tests => [:compile, :'tests:unit', :'tests:integration']

task :default => [:tests, :create_nugets]

task :ensure_nuget_key do
  raise 'missing env NUGET_KEY value' unless ENV['NUGET_KEY']
end

Albacore::Tasks::Release.new :release,
                             pkg_dir: 'build/pkg',
                             depend_on: [:tests, :create_nugets, :ensure_nuget_key],
                             nuget_exe: 'packages/NuGet.CommandLine/tools/NuGet.exe',
                             api_key: ENV['NUGET_KEY']
