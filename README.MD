https://steamcommunity.com/sharedfiles/filedetails/?id=3681283725

================================================================================
                    PUMP-TO-TANK MECHANICS GUIDE
================================================================================

Mechanical Fluid Pumps transfer fluid directly into a compatible tank 
placed below their nozzle, bypassing vanilla world-spill mechanics.

SETUP REQUIREMENTS:
-----------------
1. Place a Mechanical Fluid Pump.
2. Place a Fluid Tank (Small, Medium, or Large) in the vertical column 
   directly beneath the pump's 3x3 nozzle drop zone.
3. The connection is established automatically upon building completion.

STRICT MATCHING LOGIC (Transfer Rules):
-----------------------------------------------------------
The pump operates on a strict filter-matching system. Both the pump's UI 
toggles and the connected tank's accepted fluid must align.

* WATER MATCH:
  - Pump is set to "Water Only" AND Tank accepts Water.
  - Pipe visualizer renders Blue.

* BADWATER MATCH:
  - Pump is set to "Badwater Only" AND Tank accepts Badwater.
  - Pipe visualizer renders Red.

* UNFILTERED / MISMATCH:
  - If the pump is set to "Unfiltered" (both toggles ON) OR if the pump 
    setting does not match the tank type.
  - The pump remains active (consuming power) but transfers 0 fluid.

INDUSTRIAL LOGIC: THE POWER TRADEOFF
-----------------------------------------------------------
By moving from the manual labor of hand-operated pumps to the industrial 
might of Mechanical Fluid Pumps, your colony has chosen efficiency over 
resource conservation. 

* THE "HOT-IDLE" SYSTEM: Unlike a beaver who rests when a tank is full, 
  these industrial turbines are designed to keep spinning. Once paired 
  to a tank, the pump maintains a constant state of readiness.
* GRID MOMENTUM: Keeping the mechanical assembly under load ensures that 
  the entire power network remains stable. By never dropping the demand 
  load, your steam engines and power wheels stay locked at high-torque 
  output, ready to move fluid the exact millisecond it becomes available.
* OPERATION COST: The cost of this industrial precision is constant power 
  consumption. A primed pump draws its full rated horsepower regardless 
  of river flow or tank capacity.

SOURCE MIX DYNAMICS (Contamination Handling):
-----------------------------------------------------------
The pump dynamically scales its output based on the exact fluid composition 
of the river tiles beneath its intake.

* PURE SOURCE: Extracts fluid at 100% capacity.
* MIXED SOURCE: Acts as a fractional extractor. For example, in 
  a 70/30 Water/Badwater mix, a Water-matched pump extracts at 70% speed.
* ABSENT SOURCE: The pump remains spinning and consuming power, but the 
  liquid column vanishes and 0 units are transferred until the correct 
  fluid returns to the intake.

TECHNICAL NOTES:
---------
* FRACTIONAL PURGE: Switching a tank's accepted fluid or the pump's filter 
  wipes the internal fractional accumulators, preventing phantom fluid bugs.
* VERTICAL ALIGNMENT: The tank does not need to be placed immediately 
  beneath the pump. The mod scans the Z-axis downward from the nozzle.
* NO SPILL ENFORCEMENT: While a valid Pump-to-Tank pair exists, the vanilla 
  ground-spill function is bypassed entirely.

================================================================================