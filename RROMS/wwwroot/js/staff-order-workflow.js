/* Member 3 / US26. Status rules, prices, authorization and audit records belong to the server. */
(() => {
'use strict';
const root=document.querySelector('[data-order-workflow]');
if(!root || !window.fetch || !window.AbortController || typeof HTMLDialogElement==='undefined' || !HTMLDialogElement.prototype.showModal)return;
const $=s=>root.querySelector(s),board=$('[data-board]'),tabs=$('[data-tabs]'),filters=$('[data-filters]'),drawer=$('[data-drawer]'),dialog=$('[data-action-dialog]'),form=$('[data-action-form]');
let selected=Number(drawer.querySelector('[data-order-id]')?.dataset.orderId)||null,page=Number($('[data-snapshot]').dataset.page),stamp='',pendingCount=0;
let boardAbort,detailAbort,signalAbort,debounce,poll,busy=false,boardVersion=0,detailVersion=0,lastTrigger,dialogTrigger;
const label=s=>s==='ReadyToServe'?'Ready to Serve':s;
let noticeWrap, noticeTimer;
function notify(message,error=false,options={}){
 const kind=options.kind || (error?'error':'success');
 clearTimeout(noticeTimer);noticeWrap?.remove();
 // Use the existing shared YORU toast design; no shared styles or scripts are replaced.
 const wrap=document.createElement('div');wrap.className='yoru-toast-wrap';wrap.dataset.noticeContext=options.context || '';
 const box=document.createElement('div');box.className='yoru-toast yoru-toast--'+kind;box.setAttribute('role',error?'alert':'status');
 box.innerHTML='<span class="yoru-toast-seal" aria-hidden="true"><i class="bi"></i></span><div class="yoru-toast-body"><p class="yoru-toast-title"><span class="yoru-toast-jp"></span><span data-title></span></p><p class="yoru-toast-msg"></p></div><button type="button" class="yoru-toast-close" aria-label="Close notification"><i class="bi bi-x-lg"></i></button>';
 box.querySelector('.yoru-toast-seal i').classList.add(error?'bi-exclamation-lg':kind==='info'?'bi-info-lg':'bi-check-lg');
 box.querySelector('.yoru-toast-jp').textContent=error?'注意':kind==='info'?'お知らせ':'完了';box.querySelector('[data-title]').textContent=options.title || (error?'Please check':kind==='info'?'Notice':'All set');box.querySelector('.yoru-toast-msg').textContent=message;
 const close=()=>{wrap.remove();if(noticeWrap===wrap)noticeWrap=null;};box.querySelector('.yoru-toast-close').addEventListener('click',()=>{options.onDismiss?.();close();});
 // Optional action button inside the shared toast (same pattern as the earlier US25 queue notice).
 if(options.action){const a=document.createElement('button');a.type='button';a.className='wf-toast-action';a.textContent=options.action.label;a.addEventListener('click',()=>{close();options.action.run();});box.insertBefore(a,box.querySelector('.yoru-toast-close'));box.classList.add('wf-toast-with-action');}
 if(!error&&!options.action){const bar=document.createElement('span');bar.className='yoru-toast-bar';bar.setAttribute('aria-hidden','true');box.append(bar);noticeTimer=setTimeout(close,5500);}
 wrap.append(box);document.body.append(wrap);noticeWrap=wrap;
}
function closePicker(picker,restore=false){
 const trigger=picker.querySelector('[data-picker-trigger]');picker.querySelector('[data-picker-menu]').hidden=true;trigger.setAttribute('aria-expanded','false');
 if(restore)trigger.focus({preventScroll:true});
}
function closePickers(except=null){root.querySelectorAll('[data-picker]').forEach(p=>{if(p!==except)closePicker(p);});}
function syncPickers(){
 root.querySelectorAll('[data-picker]').forEach(p=>{
  const select=p.querySelector('select'),option=select.selectedOptions[0],trigger=p.querySelector('[data-picker-trigger]');
  p.querySelector('[data-picker-label]').textContent=option?.textContent || '';
  trigger.setAttribute('aria-label',select.getAttribute('aria-label')+': '+(option?.textContent || ''));
  p.querySelectorAll('[data-picker-option]').forEach(b=>b.setAttribute('aria-selected',String(b.dataset.value===select.value)));
 });
}
function openPicker(picker,edge=null){
 closePickers(picker);const options=[...picker.querySelectorAll('[data-picker-option]:not(:disabled)')];
 picker.querySelector('[data-picker-menu]').hidden=false;picker.querySelector('[data-picker-trigger]').setAttribute('aria-expanded','true');
 const selectedOption=options.find(b=>b.getAttribute('aria-selected')==='true');
 (edge==='first'?options[0]:edge==='last'?options.at(-1):selectedOption || options[0])?.focus({preventScroll:true});
}
function preparePickers(){
 root.querySelectorAll('[data-picker]').forEach(p=>{
  const select=p.querySelector('select'),menu=p.querySelector('[data-picker-menu]');
  for(const option of select.options){
   const b=document.createElement('button');b.type='button';b.className='wf-picker-option';b.dataset.pickerOption='';b.dataset.value=option.value;b.tabIndex=-1;b.setAttribute('role','option');b.disabled=option.disabled;
   const text=document.createElement('span');text.textContent=option.textContent;const check=document.createElement('span');check.className='wf-option-check';check.textContent='✓';check.setAttribute('aria-hidden','true');b.append(text,check);menu.append(b);
  }
  select.hidden=true;p.querySelector('[data-picker-custom]').hidden=false;
 });
 syncPickers();
}
function searchHint(message='',invalid=false){
 const hint=$('[data-search-hint]');
 hint.textContent=message;hint.hidden=!message;
 if(invalid)filters.elements.search.setAttribute('aria-invalid','true');
 else filters.elements.search.removeAttribute('aria-invalid');
}
// A complete order number jumps to that order's status (once per search) and opens its details.
let jumpedTerm='',openedTerm='';
function searchFeedback(){
 const snapshot=$('[data-snapshot]'),term=(snapshot.dataset.search || '').trim();
 if(term!==filters.elements.search.value.trim())return;
 if(!term){jumpedTerm=openedTerm='';searchHint();return;}
 const exactId=Number(snapshot.dataset.exactId)||0,exactStatus=snapshot.dataset.exactStatus||'';
 if(exactId){
  if(exactStatus!==snapshot.dataset.status&&jumpedTerm!==term){jumpedTerm=term;closeDrawer();filters.elements.status.value=exactStatus;page=1;refresh({feedback:true});return;}
  if(exactStatus===snapshot.dataset.status&&openedTerm!==term){jumpedTerm=openedTerm=term;searchHint();if(selected!==exactId)loadDetails(exactId);return;}
 }
 if(Number(snapshot.dataset.matching)!==0){searchHint();return;}
 // No match in this tab: link to the statuses that do have matches (counts already reflect the search).
 const found=[...tabs.querySelectorAll('[data-view]')].map(a=>({view:a.dataset.view,count:Number(a.querySelector('.journey-count')?.textContent)||0})).filter(x=>x.count>0&&x.view!==snapshot.dataset.status);
 if(found.length){const hint=$('[data-search-hint]');hint.replaceChildren(document.createTextNode(`No ${label(snapshot.dataset.status).toLowerCase()} orders match “${term}”. Found in: `));
  found.forEach((x,i)=>{if(i)hint.append(' · ');const b=document.createElement('button');b.type='button';b.className='wf-found-link';b.dataset.view=x.view;b.textContent=`${label(x.view)} (${x.count})`;hint.append(b);});
  hint.hidden=false;filters.elements.search.removeAttribute('aria-invalid');return;}
 const elsewhere=Number(snapshot.dataset.matchingAcrossStages)>0;
 const message=elsewhere?`No ${label(snapshot.dataset.status).toLowerCase()} orders match “${term}” with these filters. Matches exist in another status.`:`No orders match “${term}”. Try another order reference or customer name, or reset the filters.`;
 searchHint(message);
}
let cardFrame;
function sizeCards(){
 root.style.removeProperty('--wf-ticket-height');
 const cards=[...board.querySelectorAll('[data-order]')];if(!cards.length)return;
 const height=Math.ceil(Math.max(...cards.map(card=>card.getBoundingClientRect().height)));
 root.style.setProperty('--wf-ticket-height',height+'px');
}
function scheduleCardSizes(){cancelAnimationFrame(cardFrame);cardFrame=requestAnimationFrame(sizeCards);}
function params(){const p=new URLSearchParams(new FormData(filters));p.set('pageNumber',String(page));return p;}
function endpoint(handler,p=params()){const u=new URL(root.dataset.baseUrl,location.href);u.search=p;u.searchParams.set('handler',handler);return u;}
function updateUrl(){const u=new URL(root.dataset.baseUrl,location.href);u.search=params();if(selected)u.searchParams.set('orderId',selected);history.replaceState(null,'',u);}
function highlight(){board.querySelectorAll('[data-order]').forEach(c=>c.classList.toggle('selected',Number(c.dataset.order)===selected));}
function mountSnapshot(){requestAnimationFrame(alignDrawer);const s=$('[data-snapshot]');stamp=s.dataset.stamp;pendingCount=Number(s.dataset.pending);page=Number(s.dataset.page);filters.elements.status.value=s.dataset.status;for(const [k,v]of Object.entries({active:s.dataset.active,dineIn:s.dataset.dineIn,takeaway:s.dataset.takeaway}))$(`[data-metric="${k}"]`).textContent=v;board.querySelectorAll('[data-wait]').forEach(n=>n.dataset.mounted=Date.now());highlight();scheduleCardSizes();const resetUrl=new URL(root.dataset.baseUrl,location.href);resetUrl.searchParams.set('status',s.dataset.status);$('[data-reset]').href=resetUrl;}
// Desktop: line the drawer up with the first order section on the left; it then scrolls with the page.
function alignDrawer(){const t=board.querySelector('.ticket-section');const wide=matchMedia('(min-width:1101px)').matches;drawer.style.setProperty('--wf-drawer-offset',wide&&t?Math.max(0,Math.round(t.getBoundingClientRect().top-board.getBoundingClientRect().top))+'px':'0px');}
function drawerInView(){const r=drawer.getBoundingClientRect();return r.top>=0&&r.top<innerHeight*.5;}
function mountDrawer(){drawer.querySelectorAll('.wf-nojs-reason textarea').forEach(n=>n.disabled=true);alignDrawer();}
function closeDrawer(focus=false){detailAbort?.abort();detailVersion++;selected=null;drawer.hidden=true;drawer.replaceChildren();highlight();updateUrl();if(focus){const t=lastTrigger?.isConnected?lastTrigger:filters.elements.search;t.focus({preventScroll:true});const r=t.getBoundingClientRect();if(r.top<0||r.bottom>innerHeight)t.scrollIntoView({block:'center',behavior:matchMedia('(prefers-reduced-motion:reduce)').matches?'auto':'smooth'});}}
async function loadDetails(id,{focus=false,advance=false,expected}={}){
 detailAbort?.abort();detailAbort=new AbortController();const version=++detailVersion;selected=id;lastTrigger=document.activeElement;highlight();const switching=!drawer.hidden&&!!drawer.querySelector('[data-order-details]');if(switching)drawer.classList.add('is-switching');else drawer.hidden=true;drawer.setAttribute('aria-busy','true');
 try{const r=await fetch(endpoint('Details',new URLSearchParams({id:String(id)})),{signal:detailAbort.signal,headers:{'X-Requested-With':'XMLHttpRequest'}});if(!r.ok||r.redirected)throw Error(r.status===404?'This order is no longer available.':'Could not load this order. Refresh or reload this page.');const t=document.createElement('template');t.innerHTML=await r.text();if(version!==detailVersion)return;if(!t.content.querySelector('[data-order-details]'))throw Error('Your session may have expired. Reload this page.');drawer.replaceChildren(t.content);drawer.hidden=false;drawer.classList.remove('is-switching','is-entering');void drawer.offsetWidth;drawer.classList.add('is-entering');mountDrawer();updateUrl();if(focus){drawer.querySelector('h2').focus({preventScroll:true});if(matchMedia('(max-width:1100px)').matches||!drawerInView())drawer.scrollIntoView({block:'start',behavior:matchMedia('(prefers-reduced-motion:reduce)').matches?'auto':'smooth'});}if(advance){if(expected!==drawer.querySelector('[data-order-details]').dataset.status)notify('This order changed. Review its current details before choosing the next step.',true);else{const f=drawer.querySelector('[data-kind="advance"]');if(f)openAction(f);}}}
 catch(e){if(e.name!=='AbortError'&&version===detailVersion){selected=null;drawer.hidden=true;drawer.replaceChildren();highlight();notify(e.message,true);}}
 finally{if(version===detailVersion){drawer.removeAttribute('aria-busy');drawer.classList.remove('is-switching');}}
}
async function refresh({details=false,feedback=false}={}){
 boardAbort?.abort();boardAbort=new AbortController();const version=++boardVersion;board.setAttribute('aria-busy','true');
 try{const r=await fetch(endpoint('Board'),{signal:boardAbort.signal,headers:{'X-Requested-With':'XMLHttpRequest'}});if(!r.ok||r.redirected)throw Error('Could not refresh. Your displayed cards have been kept. Reload if your session has expired.');const t=document.createElement('template');t.innerHTML=await r.text();if(version!==boardVersion)return;const s=t.content.querySelector('[data-snapshot]'),nav=t.content.querySelector('[data-tabs-template]');if(!s||!nav)throw Error('Your session may have expired. Reload this page.');tabs.replaceChildren(nav.content.cloneNode(true));board.replaceChildren(t.content);mountSnapshot();updateUrl();if(feedback)searchFeedback();$('[data-updates]').hidden=true;clearUpdates();$('[data-connection]').hidden=true;if(details&&selected)await loadDetails(selected);}
 catch(e){if(e.name!=='AbortError'&&version===boardVersion)notify(e.message,true);}
 finally{if(version===boardVersion)board.removeAttribute('aria-busy');}
}
// Card button: confirm the next step without opening the drawer. The server re-checks the expected status.
let actionFromCard=false;
function openCardAction(b){const f=document.createElement('form');f.dataset.kind='advance';f.dataset.origin='card';f.dataset.title=b.dataset.title||b.textContent.trim();f.dataset.orderNumber=b.dataset.orderNumber||'';for(const [k,v] of Object.entries({id:b.dataset.cardAction,expectedStatus:b.closest('[data-order]').dataset.status,targetStatus:b.dataset.target})){const i=document.createElement('input');i.type='hidden';i.name=k;i.value=v;f.append(i);}openAction(f);}
function openAction(source){
 if(busy||dialog.open)return;const data=new FormData(source),cancel=source.dataset.kind==='cancel';actionFromCard=source.dataset.origin==='card';form.reset();for(const k of ['id','expectedStatus','targetStatus'])form.elements[k].value=data.get(k);const orderNo=source.dataset.orderNumber||drawer.querySelector('[data-order-details]')?.dataset.orderNumber||'this order';$('#wf-action-title').textContent=source.dataset.title;$('#wf-action-description').textContent=cancel?`Cancel the entire order ${orderNo}? This cannot be undone. Original items and amounts remain in history.`:data.get('targetStatus')==='Completed'?'This marks kitchen work complete. It does not record payment or close the dining session.':`Move ${orderNo} from ${label(data.get('expectedStatus'))} to ${label(data.get('targetStatus'))}.`;
 $('[data-reason-label]').hidden=!cancel;form.elements.reason.disabled=!cancel;form.elements.reason.required=cancel&&data.get('expectedStatus')==='Preparing';$('[data-reason-required]').textContent=form.elements.reason.required?'(required)':'(optional)';$('[data-dialog-error]').hidden=true;$('[data-confirm]').disabled=false;$('[data-confirm]').textContent=cancel?'Confirm cancellation':'Confirm status change';dialogTrigger=document.activeElement;dialog.showModal();(form.elements.reason.required?form.elements.reason:$('[data-confirm]')).focus();
}
function runSearch(){
 if(busy)return;
 clearTimeout(debounce);closePickers();
 if(!filters.elements.search.value.trim()){
  searchHint('Enter an order number or customer name to search.',true);
  filters.elements.search.focus({preventScroll:true});return;
 }
 searchHint();page=1;refresh({feedback:true});
}
root.addEventListener('click',e=>{
 if(e.target.closest('[data-search-submit]')){e.preventDefault();runSearch();return;}

 const pickerTrigger=e.target.closest('[data-picker-trigger]');
 if(pickerTrigger){if(busy)return;const picker=pickerTrigger.closest('[data-picker]');picker.querySelector('[data-picker-menu]').hidden?openPicker(picker):closePicker(picker,true);return;}
 const option=e.target.closest('[data-picker-option]');
 if(option){if(busy||option.disabled)return;const picker=option.closest('[data-picker]'),select=picker.querySelector('select');const changed=select.value!==option.dataset.value;select.value=option.dataset.value;syncPickers();closePicker(picker,true);if(changed)select.dispatchEvent(new Event('change',{bubbles:true}));return;}
 if(e.target.closest('[data-reset]')){e.preventDefault();if(busy)return;clearTimeout(debounce);closePickers();closeDrawer();filters.elements.search.value='';searchHint();filters.elements.dining.value='All';filters.elements.sort.value='newest';page=1;syncPickers();clearTimeout(noticeTimer);noticeWrap?.remove();noticeWrap=null;refresh();return;}

 let b=e.target.closest('[data-view]');if(b){e.preventDefault();if(busy)return;closeDrawer();filters.elements.status.value=b.dataset.view;page=1;refresh({feedback:true});return;}
 b=e.target.closest('a[data-page]');if(b){e.preventDefault();if(busy)return;page=Number(b.dataset.page);refresh().then(()=>{const top=$('.stage-heading')||board;if(top.getBoundingClientRect().top<0)top.scrollIntoView({block:'start',behavior:matchMedia('(prefers-reduced-motion:reduce)').matches?'auto':'smooth'});});return;}
 b=e.target.closest('[data-card-action]');if(b&&b.dataset.target){e.preventDefault();if(busy)return;openCardAction(b);return;}
 b=e.target.closest('[data-details],[data-card-action]');if(b){e.preventDefault();if(busy)return;loadDetails(Number(b.dataset.details||b.dataset.cardAction),{focus:true,advance:b.hasAttribute('data-card-action'),expected:b.closest('[data-order]').dataset.status});return;}
 b=e.target.closest('[data-history-toggle]');if(b){const list=b.closest('[data-history-list]'),open=!list.classList.contains('is-expanded');list.classList.toggle('is-expanded',open);b.setAttribute('aria-expanded',String(open));b.querySelector('[data-more-label]').hidden=open;b.querySelector('[data-less-label]').hidden=!open;return;}
 b=e.target.closest('[data-dish-toggle]');if(b){const list=b.closest('[data-dish-list]'),open=!list.classList.contains('is-expanded');list.classList.toggle('is-expanded',open);b.setAttribute('aria-expanded',String(open));b.querySelector('[data-dish-more-label]').hidden=open;b.querySelector('[data-dish-less-label]').hidden=!open;if(!open)b.scrollIntoView({block:'nearest'});return;}
 if(e.target.closest('[data-close-drawer]')){e.preventDefault();if(!busy)closeDrawer(true);}
 if(e.target.closest('[data-refresh]')&&!busy)refresh({details:true,feedback:true});
 if(e.target.closest('[data-retry]'))checkSignal();
 if(e.target.closest('[data-close-dialog]')&&!busy)dialog.close();
});
document.addEventListener('click',e=>{if(!e.target.closest('[data-picker]'))closePickers();});
root.addEventListener('keydown',e=>{
 const picker=e.target.closest('[data-picker]');if(!picker||busy)return;
 const menu=picker.querySelector('[data-picker-menu]'),options=[...menu.querySelectorAll('[data-picker-option]:not(:disabled)')];
 if(e.key==='Escape'&&!menu.hidden){e.preventDefault();e.stopPropagation();closePicker(picker,true);return;}
 if(e.key==='Tab'&&!menu.hidden){closePicker(picker,true);return;}
 if(!['ArrowDown','ArrowUp','Home','End'].includes(e.key))return;
 e.preventDefault();
 if(menu.hidden){openPicker(picker,e.key==='Home'?'first':e.key==='End'?'last':null);return;}
 let index=options.indexOf(document.activeElement);index=e.key==='Home'?0:e.key==='End'?options.length-1:e.key==='ArrowDown'?(index+1)%options.length:(index-1+options.length)%options.length;
 options[index]?.focus({preventScroll:true});
});
drawer.addEventListener('submit',e=>{const f=e.target.closest('[data-change-form]');if(f){e.preventDefault();openAction(f);}});
filters.addEventListener('submit',e=>{e.preventDefault();if(!busy){searchHint();clearTimeout(debounce);closePickers();refresh({details:true,feedback:true});}});
filters.elements.search.addEventListener('keydown',e=>{
 if(e.key!=='Enter'||e.isComposing)return;
 e.preventDefault();runSearch();
});
filters.elements.search.addEventListener('input',()=>{if(busy)return;searchHint();clearTimeout(debounce);page=1;debounce=setTimeout(()=>refresh({feedback:true}),350);});
for(const n of ['dining','sort'])filters.elements[n].addEventListener('change',()=>{if(!busy){page=1;refresh({feedback:true});}});
dialog.addEventListener('cancel',e=>{if(busy)e.preventDefault();});
dialog.addEventListener('close',()=>{if(dialogTrigger?.isConnected)dialogTrigger.focus({preventScroll:true});});
form.addEventListener('submit',async e=>{
 e.preventDefault();if(busy||!form.reportValidity())return;busy=true;clearTimeout(debounce);boardAbort?.abort();detailAbort?.abort();
 const data=new FormData(form),confirm=$('[data-confirm]');let stale=false;confirm.disabled=true;confirm.textContent='Saving…';dialog.querySelectorAll('[data-close-dialog]').forEach(b=>b.disabled=true);filters.querySelectorAll('input,select,button').forEach(b=>b.disabled=true);$('[data-dialog-error]').hidden=true;
    try {
        const r = await fetch(endpoint('ChangeStatus', new URLSearchParams()), { method: 'POST', body: data, headers: { 'X-Requested-With': 'XMLHttpRequest' } }); const result = await r.json().catch(() => null); if (!r.ok || !result?.success) { stale = r.status !== 400; throw Error(result?.message || 'We could not confirm the result. Refresh this order before trying again.'); } dialog.close(); const doneId = Number(data.get('id')), doneStatus = data.get('targetStatus');
        // After a successful change, close the drawer for that order and stay on this tab; the toast offers to open it in its new status.
        if (selected === doneId) closeDrawer();
        notify(result.message, false, { context: 'status', action: { label: 'View order', run: () => { if (busy) return; filters.elements.status.value = doneStatus; page = 1; selected = doneId; refresh({ details: true }); } } });
        noticeTimer = setTimeout(() => { if (noticeWrap?.dataset.noticeContext === 'status') { noticeWrap.remove(); noticeWrap = null; } }, 6000);
    }
 catch(error){stale=stale||error instanceof TypeError;$('[data-dialog-error]').textContent=error.message||'Could not confirm the result. Refresh this order.';$('[data-dialog-error]').hidden=false;}
 finally{busy=false;filters.querySelectorAll('input,select,button').forEach(b=>b.disabled=false);dialog.querySelectorAll('[data-close-dialog]').forEach(b=>b.disabled=false);confirm.disabled=stale;confirm.textContent=data.get('targetStatus')==='Cancelled'?'Confirm cancellation':'Confirm status change';if(!dialog.open||stale)await refresh({details:true});}
});
// Incoming-order updates use the team's shared YORU toast instead of an inline banner.
let latestUpdateStamp='',dismissedUpdateStamp='';
function showUpdates(signalStamp,message,isNew){latestUpdateStamp=signalStamp;if(signalStamp===dismissedUpdateStamp)return;const title=isNew?'New orders received':'Order queue updated';
 if(noticeWrap?.dataset.noticeContext==='updates'){noticeWrap.querySelector('.yoru-toast-msg').textContent=message;noticeWrap.querySelector('[data-title]').textContent=title;return;}
 if(noticeWrap)return; // let a status-change message finish first; the next check shows the update.
 notify(message,false,{kind:'info',context:'updates',title,action:{label:'Refresh to show',run:()=>{if(!busy)refresh({details:true,feedback:true});}},onDismiss:()=>{dismissedUpdateStamp=latestUpdateStamp;}});}
function clearUpdates(){if(noticeWrap?.dataset.noticeContext==='updates'){noticeWrap.remove();noticeWrap=null;}}
async function checkSignal(){
 clearTimeout(poll);if(document.hidden||busy){poll=setTimeout(checkSignal,15000);return;}signalAbort?.abort();signalAbort=new AbortController();const controller=signalAbort,timeout=setTimeout(()=>controller.abort(),10000);
 try{const r=await fetch(endpoint('Signal',new URLSearchParams()),{signal:controller.signal,headers:{'X-Requested-With':'XMLHttpRequest'}});if(!r.ok||r.redirected)throw Error();const s=await r.json();if(!s.stamp)throw Error();$('[data-connection]').hidden=true;if(s.stamp!==stamp){const n=Number(s.pending)-pendingCount;showUpdates(s.stamp,n>0?`${n} more pending ${n===1?'order is':'orders are'} waiting. Your current cards have stayed in place.`:'Orders have changed. Your current cards have stayed in place.',n>0);}}
 catch(e){if(!document.hidden)$('[data-connection]').hidden=false;}
 finally{clearTimeout(timeout);poll=setTimeout(checkSignal,15000);}
}
function updateWaiting(){board.querySelectorAll('[data-wait]').forEach(n=>{const m=Math.max(0,Math.floor((Number(n.dataset.wait)+(Date.now()-Number(n.dataset.mounted))/1000)/60));n.textContent=m<1?'Just received':m<60?`${m} min`:`${Math.floor(m/60)} hr${m%60?' '+m%60+' min':''}`;const c=n.closest('[data-wait-chip]');if(c)c.dataset.level=m>=20?'long':m>=10?'warn':'normal';});}
document.addEventListener('visibilitychange',()=>{clearTimeout(poll);if(document.hidden)signalAbort?.abort();else{updateWaiting();checkSignal();}});
if(window.ResizeObserver){let lastWidth=-1;new ResizeObserver(entries=>{const width=entries[0].contentRect.width;if(Math.abs(width-lastWidth)>.5){lastWidth=width;scheduleCardSizes();alignDrawer();}}).observe(board);}
else window.addEventListener('resize',scheduleCardSizes);
if(document.fonts){document.fonts.ready.then(scheduleCardSizes);document.fonts.addEventListener?.('loadingdone',scheduleCardSizes);}
root.classList.add('js-ready');preparePickers();mountSnapshot();mountDrawer();searchFeedback();setInterval(updateWaiting,30000);poll=setTimeout(checkSignal,15000);
})();
