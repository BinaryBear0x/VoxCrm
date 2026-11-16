(() => {
'use strict';
const patient=document.getElementById('visit-patient'),owner=document.getElementById('visit-owner'),data=JSON.parse(document.getElementById('visit-owners-data').textContent);
const previous=owner.value || document.querySelector('[name="OwnerId"]')?.dataset.selected;
function owners(selected){owner.replaceChildren(new Option('Hasta sahibini seçin',''));const list=data.find(x=>x.id===patient.value)?.owners||[];for(const o of list)owner.add(new Option(o.label,o.id));if(selected&&list.some(x=>x.id===selected))owner.value=selected;else if(list.length===1)owner.value=list[0].id;}
owners(previous);patient.addEventListener('change',()=>owners());
const start=document.getElementById('speech-start'),stop=document.getElementById('speech-stop'),status=document.getElementById('speech-status'),notes=document.getElementById('Notes');
const API=window.SpeechRecognition||window.webkitSpeechRecognition;let recognition;
if(!API){start.disabled=true;status.textContent='Bu tarayıcı ses tanımayı desteklemiyor. Notu yazarak devam edebilirsiniz.';}
else {recognition=new API();recognition.lang='tr-TR';recognition.continuous=true;recognition.interimResults=false;
recognition.onresult=e=>{for(let i=e.resultIndex;i<e.results.length;i++)if(e.results[i].isFinal)notes.value=(notes.value+' '+e.results[i][0].transcript).trim().slice(0,4000);};
recognition.onerror=e=>{status.textContent=e.error==='not-allowed'?'Mikrofon izni verilmedi. Tarayıcı izinlerini kontrol edin veya notu yazın.':'Ses alınamadı ('+e.error+'). Yazıyla devam edebilirsiniz.';};
recognition.onend=()=>{start.disabled=false;stop.disabled=true;};
start.addEventListener('click',()=>{if(!document.getElementById('speech-consent').checked){status.textContent='Başlamadan önce ses işleme bilgisini onaylayın.';return;}try{recognition.start();start.disabled=true;stop.disabled=false;status.textContent='Dinleniyor… Bitirince Durdur düğmesine basın ve metni kontrol edin.';}catch{status.textContent='Ses tanıma başlatılamadı. Yazıyla devam edebilirsiniz.';}});stop.addEventListener('click',()=>{recognition.stop();status.textContent='Ses alımı durdu. Metni kontrol edin.';});}
document.getElementById('visit-form').addEventListener('submit',()=>recognition?.stop());window.addEventListener('pagehide',()=>recognition?.abort());
document.getElementById('visit-parse').addEventListener('click',async()=>{const body=new URLSearchParams({text:notes.value,__RequestVerificationToken:document.querySelector('[name="__RequestVerificationToken"]').value});try{const response=await fetch('/QuickVisit/Parse',{method:'POST',body});if(!response.ok)throw Error();const p=await response.json();for(const [key,id] of [['weight','Weight'],['amount','Amount'],['paid','Paid']])document.getElementById(id).value=p[key]??'';status.textContent='Anlaşılabilen sayılar taslağa alındı. Ödeme yöntemini, tarihleri ve uygulamaları mutlaka kontrol edin. Henüz kayıt yapılmadı.';}catch{status.textContent='Metin ayrıştırılamadı. Alanları kendiniz doldurabilirsiniz.';}});
})();
