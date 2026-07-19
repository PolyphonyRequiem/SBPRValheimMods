// Exercise the LIVE T027 net48 provider (loaded in the running client) for every decision branch.
var hs=System.AppDomain.CurrentDomain.GetAssemblies().First(a=>a.GetName().Name=="SBPR.Niflheim.HomesteadStones");
var provT=hs.GetType("SBPR.Niflheim.HomesteadStones.Adapters.Archer.ProjectileRecoveryProvider");
var catT=hs.GetType("SBPR.Niflheim.HomesteadStones.Domain.Content.HomesteadProgressionCatalog");
var provenT=hs.GetType("SBPR.Niflheim.HomesteadStones.Adapters.Archer.ConsumedArrowProvenance");
var surfT=hs.GetType("SBPR.Niflheim.HomesteadStones.Adapters.Archer.RecoverySurface");
var cat=System.Activator.CreateInstance(catT);
var prov=System.Activator.CreateInstance(provT,new object[]{cat}); // default chance 0.5
// build ArrowWood provenance
var pv=System.Activator.CreateInstance(provenT,new object[]{"ArrowWood",3,1,42.0,777001L,"QA_PROV_TAG","k=v"});
var pvIneligible=System.Activator.CreateInstance(provenT,new object[]{"ArrowIron",1,0,10.0,0L,"","" });
var resolve=provT.GetMethod("Resolve");
System.Func<object,object,int,bool,double,string> R = (owned,pvn,surf,trw,roll)=>{
  var d=resolve.Invoke(prov,new object[]{owned,pvn,System.Enum.ToObject(surfT,surf),trw,roll});
  var oc=d.GetType().GetProperty("Outcome").GetValue(d);
  var rc=d.GetType().GetProperty("RecoveredCount").GetValue(d);
  var ra=d.GetType().GetProperty("RecoveredArrow").GetValue(d);
  var raId=ra.GetType().GetProperty("ItemId").GetValue(ra);
  var raQ=ra.GetType().GetProperty("Quality").GetValue(ra);
  var raCr=ra.GetType().GetProperty("CrafterName").GetValue(ra);
  return "outcome="+oc+" recovered="+rc+" arrow="+raId+"(q"+raQ+","+raCr+")";
};
// SolidStructure=0 Ground=1 Creature=2 ShieldBlocked=3 Water=4 LostOrExpired=5 ArcheryTarget=6
Log("chance="+provT.GetProperty("RecoveryChance").GetValue(prov));
Log("S1 owner+ArrowWood+SolidStructure+lowRoll(0.1): "+R(true,pv,0,false,0.1));
Log("S1 owner+ArrowWood+Ground+lowRoll(0.1):        "+R(true,pv,1,false,0.1));
Log("S1 owner+ArrowWood+Solid+highRoll(0.9)=RollFail:"+R(true,pv,0,false,0.9));
Log("S2 owner+ArrowWood+Water(no roll):             "+R(true,pv,4,false,0.1));
Log("S2 owner+ArrowWood+Lost/TTL(no roll):          "+R(true,pv,5,false,0.1));
Log("S3 owner+ArrowWood+ArcheryTarget+targetReturnWon(suppress):"+R(true,pv,6,true,0.1));
Log("NonOwner+ArrowWood+Solid+lowRoll(vanilla):     "+R(false,pv,0,false,0.1));
Log("Owner+IneligibleArrow+Solid+lowRoll:           "+R(true,pvIneligible,0,false,0.1));
"done"
