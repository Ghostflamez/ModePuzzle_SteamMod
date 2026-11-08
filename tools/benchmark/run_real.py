import glob, subprocess, sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT))
import build
data=Path(build.unity_data_dir()); managed=Path(build.game_dir())/'Probably Stolen_Data'/'Managed'
exe=ROOT/'build/RealBenchmark.exe'
subprocess.run([str(data/'NetCoreRuntime/dotnet.exe'),str(data/'DotNetSdkRoslyn/csc.dll'),'-nologo','-noconfig','-nostdlib','-optimize+','-langversion:9.0','-main:RealBenchmark',f'-out:{exe}',*[f'-r:{managed/r}' for r in ('mscorlib.dll','System.dll','System.Core.dll','netstandard.dll')],str(ROOT/'tools/parity/Parity.cs'),str(ROOT/'tools/benchmark/RealBenchmark.cs'),*glob.glob(str(ROOT/'src/Engine/*.cs'))],check=True)
with (ROOT/'build/real_benchmark.csv').open('w',encoding='utf-8') as out:
    result=subprocess.run([str(data/'MonoBleedingEdge/bin/mono.exe'),str(exe),str(ROOT/'build/real_cases.json')],stdout=out)
print((ROOT/'build/real_benchmark.csv').read_text(encoding='utf-8'))
sys.exit(result.returncode)
