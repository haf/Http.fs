require 'bundler/setup'

require 'albacore'
require 'albacore/tasks/release'
require 'albacore/tasks/versionizer'
require 'albacore/ext/teamcity'

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

desc 'Perform fast build (warn: doesn\'t d/l deps)'
build :quick_compile do |b|
  b.prop 'Configuration', Configuration
  b.logging = 'detailed'
  b.sln     = 'Http.fs.sln'
  if HttpFsStrongName
    b.prop 'AssemblyStrongName', 'true'
  end
end

task :paket_bootstrap do
  system 'Tools/paket.bootstrapper.exe', clr_command: true unless   File.exists? 'Tools/paket.exe'
end

desc 'restore all nugets as per the packages.config files'
task :restore => :paket_bootstrap do
  system 'Tools/paket.exe', 'restore', clr_command: true
end

task :yolo do
  system %{ruby -pi.bak -e "gsub(/module internal YoLo/, 'module internal HttpFs.YoLo')" paket-files/haf/YoLo/YoLo.fs} \
    unless Albacore.windows?
end

desc 'Perform full build'
build :compile => [:versioning, :restore, :assembly_info, :yolo] do |b|
  b.prop 'Configuration', Configuration
  b.logging = 'normal'
  b.sln = 'Http.fs.sln'
end

directory 'build/pkg'

desc 'package nugets - finds all projects and package them'
nugets_pack :create_nugets => ['build/pkg', :versioning, :compile] do |p|
  p.configuration = Configuration
  p.files   = FileList['HttpFs/HttpFs.fsproj'].
    exclude(/Tests/)
  p.out     = 'build/pkg'
  p.exe     = 'packages/NuGet.CommandLine/tools/NuGet.exe'
  p.with_metadata do |m|
    m.id          = 'Http.fs'
    m.title       = 'Http.fs'
    m.description = 'A simple, functional HTTP client library for F#'
    m.authors     = 'Grant Crofton, Henrik Feldt'
    m.project_url = 'https://github.com/relentless/Http.fs'
    m.version     = ENV['NUGET_VERSION']
  end
end

namespace :tests do
  task :integration do
    system 'packages/NUnit.Runners/tools/nunit-console.exe', '--noshadow', %W|
           HttpFs.IntegrationTests/bin/#{Configuration}/HttpFs.IntegrationTests.dll|,
           clr_command: true
  end
  task :unit do
    system "HttpFs.UnitTests/bin/#{Configuration}/HttpFs.UnitTests.exe", clr_command: true
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
