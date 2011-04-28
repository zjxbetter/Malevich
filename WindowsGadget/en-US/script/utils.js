function init()
{
    checkDockState(); // check the initial state
	
	// setup the event handlers for docking later
	System.Gadget.onDock = dockGadget;
	System.Gadget.onUndock = undockGadget;
	
	// if using a flyout function, set the 
}

function dockGadget()
{
    // TODO: add your docking functions here
    var oBody = document.body.style;
	oBody.width = '120px';
	oBody.height = '100px';
	
    showOrHide("docked", true);
    showOrHide("undocked", false);
}

function undockGadget()
{
    // TODO: add your undocking functions here
    var oBody = document.body.style;
	oBody.width = '120px';
	oBody.height = '100px';
	
    showOrHide("undocked", false);
    showOrHide("docked", true);
}

function checkDockState()
{
    if (System.Gadget.docked) {
        dockGadget();
    }
    else {
        undockGadget();
    }
}

function showOrHide(oHTMLElement, bShowOrHide) 
{
  try 
  {
	if (typeof(oHTMLElement)=="string") 
	{ 
	  oHTMLElement = document.getElementById(oHTMLElement); 
	}
	if (oHTMLElement && oHTMLElement.style) 
	{
	  if (bShowOrHide == 'inherit') 
	  {
		oHTMLElement.style.visibility = 'inherit';
	  } 
	  else 
	  {
		if (bShowOrHide)
		{
		  oHTMLElement.style.visibility = 'visible';
		}
		else
		{
		  oHTMLElement.style.visibility = 'hidden';
		}
		try 
		{
		  if (bShowOrHide)
		  {
			oHTMLElement.style.display = 'block';
		  }
		  else
		  {
			oHTMLElement.style.display = 'none';
		  }
		}
		catch (ex) 
		{
		}
	  }
	}
  }
  catch (ex) 
  {
  }
}
function flyout()
{
    System.Gadget.Flyout.file = "flyout.html";
    System.Gadget.Flyout.show = true;   
}