"""Summarize measured frame intervals; never labels CPU submission as photon latency."""
import argparse,csv,json,statistics
from pathlib import Path
def summary(rows, key):
 values=sorted(float(r[key]) for r in rows if r.get(key))
 if not values:return None
 def q(p):return values[round((len(values)-1)*p)]
 return {'n':len(values),'median':q(.5),'p95':q(.95),'p99':q(.99)}
def main():
    p=argparse.ArgumentParser();p.add_argument('csv');p.add_argument('--warmup',type=float,default=3);p.add_argument('--output');a=p.parse_args()
    with open(a.csv,encoding='utf-8-sig') as f: rows=list(csv.DictReader(line for line in f if not line.startswith('#')))
    start=float(rows[0]['realtime_s']);rows=[r for r in rows if float(r['realtime_s'])-start>=a.warmup]
    result={k:summary(rows, k) for k in ['frame_ms','cpu_frame_ms','gpu_frame_ms','solver_cpu_ms','contact_sample_age_ms','contact_pairs','reaction_n','rms_displacement_m','max_volume_change']}
    result['notes']='frame_ms is real frame interval; sample age is NOT tracking latency or motion-to-photon. Quality columns are sampled every 0.2 s and repeat between samples. Fixed capture-rate validation is not a performance benchmark.'
    s=json.dumps(result,indent=2);print(s)
    if a.output:Path(a.output).write_text(s)

if __name__ == "__main__":
    main()
