"""Extract only a requested PLY from the official PhysDreamer ZIP using HTTP ranges.
Mirror transports the same public repository; ZIP CRC is verified by zipfile.
No pickle/checkpoint deserialization. Run --list first, then --member exact/path.ply.
"""
import argparse, io, subprocess, zipfile, pathlib, hashlib, json

import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from repository_paths import RAW_DATA, PROCESSED_DATA, ARTIFACTS

class RemoteZip(io.RawIOBase):
    def __init__(self,url,size):
        self.size=size;self.pos=0
        # Resolve once; repeated mirror redirects can be much slower than range reads.
        resolved=subprocess.run(['curl','--fail','--silent','--show-error','--location','--head','--max-time','60','--output','/dev/null','--write-out','%{url_effective}',url],capture_output=True,check=True)
        self.url=resolved.stdout.decode().strip()
    def seekable(self): return True
    def readable(self): return True
    def tell(self): return self.pos
    def seek(self,offset,whence=0):
        self.pos=offset if whence==0 else self.pos+offset if whence==1 else self.size+offset
        return self.pos
    def read(self,n=-1):
        if n<0:n=self.size-self.pos
        if n==0:return b''
        end=min(self.size,self.pos+n)-1
        cache=ARTIFACTS/'cache'/'physdreamer'/hashlib.sha256(self.url.encode()).hexdigest()[:16];cache.mkdir(parents=True,exist_ok=True)
        filename=cache/f'{self.pos}-{end}.bin'
        if filename.exists():
            data=filename.read_bytes();self.pos+=len(data);return data
        result=subprocess.run(['curl','--fail','--silent','--show-error','--location','--max-time','600','--range',f'{self.pos}-{end}',self.url],capture_output=True,check=True)
        data=result.stdout
        if len(data)!=end-self.pos+1:raise IOError(f'Range length mismatch: {len(data)} != {end-self.pos+1}')
        filename.write_bytes(data)
        self.pos+=len(data);return data

def main():
    p=argparse.ArgumentParser();p.add_argument('--list',action='store_true');p.add_argument('--member');p.add_argument('--output',default=str(RAW_DATA / 'flower' / 'carnation.ply'))
    p.add_argument('--url',default='https://hf-mirror.com/datasets/YunjinZhang/PhysDreamer/resolve/main/physics_dreamer.zip');p.add_argument('--size',type=int,default=7515831594);a=p.parse_args()
    with zipfile.ZipFile(RemoteZip(a.url,a.size)) as archive:
        if a.list:
            for f in archive.infolist():
                if f.filename.endswith(('.ply','.json')):print(f.filename,f.file_size,flush=True)
        if a.member:
            content=archive.read(a.member);target=pathlib.Path(a.output);target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(content)
            target.with_suffix('.source.json').write_text(json.dumps({'source':'https://huggingface.co/datasets/YunjinZhang/PhysDreamer','transport':a.url,'member':a.member,'sha256':hashlib.sha256(content).hexdigest()},indent=2))
            print(str(target),len(content),flush=True)

if __name__ == "__main__":
    main()
