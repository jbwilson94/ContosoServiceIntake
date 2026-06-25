$(document).ready(function() {
  if (!window.ContosoCommon) {
    return;
  }

  window.ContosoCommon.addBackToHomeButton();

  window.ContosoCommon.makeRequired("#firstname");
  window.ContosoCommon.makeRequired("#lastname");
  window.ContosoCommon.makeRequired("#emailaddress1");
  window.ContosoCommon.makeRequired("#address1_line1");
  window.ContosoCommon.makeRequired("#address1_city");
  window.ContosoCommon.makeRequired("#address1_stateorprovince");
  window.ContosoCommon.makeRequired("#address1_postalcode");
  window.ContosoCommon.makeRequired("#address1_country");

  window.ContosoCommon.formatPhoneInput("#telephone1");
  window.ContosoCommon.formatPhoneInput("#address1_telephone1");
  window.ContosoCommon.formatPostalCodeInput("#address1_postalcode");
});
