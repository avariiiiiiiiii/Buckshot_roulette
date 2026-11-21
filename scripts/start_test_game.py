"""Build a workspace-local test profile and launch the installed game visibly.

Only copies BepInEx/REPOLib and the compiled mod into scratch/playtest: the game itself
gets no debug spawns, the shop and level spawn come from the regular game rules.
"""
import configparser
import json
import os
from pathlib import Path
import shutil
import subprocess
from datetime import datetime

ROOT=Path(__file__).resolve().parents[1]
GAME=Path(r'C:\Program Files (x86)\Steam\steamapps\common\REPO')
SOURCE=Path(os.environ['APPDATA'])/'Thunderstore Mod Manager/DataFolder/REPO/profiles/buckshot/BepInEx'
RUNTIME=ROOT/'scratch/playtest'
BEP=RUNTIME/'BepInEx'

identity=subprocess.check_output(['whoami'],text=True).strip()
if identity.rsplit('\\',1)[-1].lower().startswith('codexsandbox'):
    raise SystemExit('Steam needs the signed-in Windows user. This isolated agent account cannot launch the user Steam session. Run start-test.cmd from your Windows desktop.')

def running(name):
    result=subprocess.run(['tasklist','/FI',f'IMAGENAME eq {name}','/FO','CSV','/NH'],capture_output=True,text=True)
    return name.lower() in result.stdout.lower()

def ini(path):
    config=configparser.ConfigParser(interpolation=None)
    config.optionxform=str
    config.read(path,encoding='utf-8-sig')
    return config

if running('REPO.exe'):
    raise SystemExit('REPO is already running; close it before rebuilding the test profile.')
for directory in ['core','plugins/Zehs-REPOLib']:
    shutil.copytree(SOURCE/directory,BEP/directory,dirs_exist_ok=True)
(BEP/'config').mkdir(exist_ok=True)
for path in (SOURCE/'config').glob('*.cfg'):shutil.copy2(path,BEP/'config'/path.name)
path=BEP/'config/BepInEx.cfg';config=ini(path)
config['Logging.Console']['Enabled']='false'
with path.open('w',encoding='utf-8') as output:config.write(output)

subprocess.run(['dotnet','msbuild',str(ROOT/'src/BuckshotRoulette.Plugin/BuckshotRoulette.Plugin.csproj'),
    '-t:DeployToProfile',f'-p:GameDir={GAME}',f'-p:ProfileDir={RUNTIME}',
    '-verbosity:quiet','-consoleLoggerParameters:ErrorsOnly'],check=True)
steam=GAME.parents[2]/'steam.exe'
if not running('steam.exe') and steam.is_file():
    startup=subprocess.STARTUPINFO();startup.dwFlags|=subprocess.STARTF_USESHOWWINDOW;startup.wShowWindow=0
    subprocess.Popen([str(steam),'-silent'],startupinfo=startup,creationflags=subprocess.CREATE_NO_WINDOW)
env=os.environ.copy();env.update(SteamAppId='3241660',SteamGameId='3241660')
log=RUNTIME/'Player.log'
process=subprocess.Popen([str(GAME/'REPO.exe'),'--doorstop-enable','true',
    '--doorstop-target-assembly',str(BEP/'core/BepInEx.Preloader.dll'),'-screen-fullscreen','0',
    '-screen-width','1280','-screen-height','720','-logFile',str(log)],cwd=GAME,env=env)
(RUNTIME/'launch.json').write_text(json.dumps({'processId':process.pid,'profile':str(RUNTIME),
    'playerLog':str(log),'started':datetime.now().isoformat()},indent=2),encoding='utf-8')
print(f'Started REPO pid={process.pid}; test profile={RUNTIME}',flush=True)
