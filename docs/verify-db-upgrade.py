"""Local-only upgrade rehearsal. Restores trusted dumps into new UUID databases; never connects remotely."""
import base64,hashlib,json,os,re,subprocess,uuid
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
PG=Path('C:/Program Files/PostgreSQL/18/bin')
PRIVATE=ROOT.parent/'VoxCRM-local-private'
REPORT=ROOT/'docs/db-upgrade-rehearsal.json'
os.environ['PGCLIENTENCODING']='UTF8'
BASE=['-h','127.0.0.1','-p','55432','-U','voxcrm']

def command(args,**kw):
 r=subprocess.run([str(x) for x in args],capture_output=True,text=True,encoding='utf-8',errors='replace',**kw)
 return r

def query(database,sql):
 r=command([PG/'psql.exe',*BASE,'-X','-q','-A','-t','-v','ON_ERROR_STOP=1','-d',database],input=sql)
 if r.returncode:raise RuntimeError('Local SQL failed: '+r.stderr[:600])
 return r.stdout.strip()
def ident(s):return '"'+s.replace('"','""')+'"'
def structure(db):
 sql="SELECT encode(convert_to(json_agg(t)::text,'UTF8'),'base64') FROM (SELECT table_name,array_agg(column_name ORDER BY ordinal_position) AS columns FROM information_schema.columns WHERE table_schema='public' GROUP BY table_name ORDER BY table_name)t"
 return {r['table_name']:r['columns'] for r in json.loads(base64.b64decode(query(db,sql)).decode('utf-8'))}
def fingerprint(db,table,cols):
 sql="SET timezone='UTC';SELECT json_build_object('rows',count(*),'hash',md5(coalesce(string_agg(h,'' ORDER BY h),''))) FROM (SELECT md5(row_to_json(t)::text) h FROM (SELECT "+','.join(map(ident,cols))+" FROM public."+ident(table)+")t)s"
 return json.loads(query(db,sql))

def migration_history(db):
 return json.loads(query(db,"SELECT coalesce(json_agg(\"MigrationId\" ORDER BY \"MigrationId\"),'[]'::json) FROM \"__EFMigrationsHistory\""))

cases=[('downloaded_snapshot',PRIVATE/'voxcrm-db.dump'),('before_latest_fix',sorted(PRIVATE.glob('pre-five-fixes-*.dump'))[-1])]
results=[]
for label,backup in cases:
 name='voxcrm_upgrade_qa_'+uuid.uuid4().hex
 assert re.fullmatch('voxcrm_upgrade_qa_[0-9a-f]{32}',name)
 item={'case':label,'database':name,'backup':backup.name,'backup_sha256':hashlib.sha256(backup.read_bytes()).hexdigest(),'postgres':'18','production_compose_postgres':'16'}
 try:
  query('postgres','CREATE DATABASE '+ident(name))
  restored=command([PG/'pg_restore.exe',*BASE,'--no-owner','--no-privileges','--exit-on-error','-d',name,backup])
  (PRIVATE/(name+'-restore.log')).write_text(restored.stdout+restored.stderr,encoding='utf-8')
  if restored.returncode:raise RuntimeError('Restore failed; see private log.')
  before=structure(name);item['migrations_before']=migration_history(name)
  hashes={t:fingerprint(name,t,c) for t,c in before.items() if t!='__EFMigrationsHistory'}
  env=os.environ.copy();env.update({'ASPNETCORE_ENVIRONMENT':'Development','ConnectionStrings__DefaultConnection':f'Host=127.0.0.1;Port=55432;Database={name};Username=voxcrm','BackgroundJobs__Enabled':'false','SystemHealth__EnableDockerStatus':'false','DataSeeding__SeedDemoData':'false','DataSeeding__SystemAdmin__Enabled':'false','DataSeeding__ProductionDealer__Enabled':'false','WhatsAppGateway__BaseUrl':'http://127.0.0.1:8088','PiiEncryption__KeyFile':str(PRIVATE/'pii.key')})
  dll=ROOT/'VoxCrm.Web/bin/Debug/net10.0/VoxCrm.Web.dll'
  run=command(['C:/Program Files/dotnet/dotnet.exe',dll,'--migrate-only'],cwd=ROOT/'VoxCrm.Web',env=env)
  (PRIVATE/(name+'-migration.log')).write_text(run.stdout+run.stderr,encoding='utf-8')
  item['migration_exit']=run.returncode
  if run.returncode:raise RuntimeError('Upgrade failed; see private migration log.')
  after=structure(name);item['migrations_after']=migration_history(name)
  item['new_tables']=sorted(set(after)-set(before));item['missing_tables']=sorted(set(before)-set(after))
  item['new_columns']={t:sorted(set(after[t])-set(before[t])) for t in before if t in after and set(after[t])!=set(before[t])}
  item['comparisons']={t:{'before':h,'after':fingerprint(name,t,before[t])} for t,h in hashes.items() if t in after and set(before[t]).issubset(after[t])}
  item['changed_existing_data']=[t for t,v in item['comparisons'].items() if v['before']!=v['after']]
  item['missing_columns']={t:sorted(set(before[t])-set(after.get(t,[]))) for t in before if set(before[t])-set(after.get(t,[]))}
  again=command(['C:/Program Files/dotnet/dotnet.exe',dll,'--migrate-only'],cwd=ROOT/'VoxCrm.Web',env=env)
  (PRIVATE/(name+'-repeat.log')).write_text(again.stdout+again.stderr,encoding='utf-8')
  item['repeat_exit']=again.returncode
  item['repeat_preserves_data']=all(fingerprint(name,t,before[t])==v['after'] for t,v in item['comparisons'].items())
  item['status']='passed' if not(item['missing_tables'] or item['missing_columns'] or item['changed_existing_data']) and again.returncode==0 and item['repeat_preserves_data'] else 'needs_review'
 except Exception as ex:item['status']='failed';item['error']=str(ex)
 results.append(item);REPORT.write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf-8')
 print(json.dumps({k:v for k,v in item.items() if k in ['case','database','status','migration_exit','repeat_exit','changed_existing_data','missing_tables','error','repeat_preserves_data']}),flush=True)
