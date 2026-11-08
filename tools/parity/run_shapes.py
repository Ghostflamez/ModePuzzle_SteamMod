import glob, os, subprocess, sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT))
import build
data=Path(build.unity_data_dir()); managed=Path(build.game_dir())/'Probably Stolen_Data'/'Managed'
exe=ROOT/'build/ShapeChecks.exe'
sources=['tools/parity/ShapeChecks.cs','src/Model/Snapshot.cs','src/Game/EngineBridge.cs']
subprocess.run([str(data/'NetCoreRuntime/dotnet.exe'),str(data/'DotNetSdkRoslyn/csc.dll'),'-nologo','-noconfig','-nostdlib','-optimize+','-langversion:9.0',f'-out:{exe}',*[f'-r:{managed/r}' for r in ('mscorlib.dll','System.dll','System.Core.dll','netstandard.dll','Assembly-CSharp.dll')],*[str(ROOT/p) for p in sources],*glob.glob(str(ROOT/'src/Engine/*.cs'))],check=True)
env=dict(os.environ,MONO_PATH=str(managed))
sys.exit(subprocess.run([str(data/'MonoBleedingEdge/bin/mono.exe'),str(exe)],env=env).returncode)
