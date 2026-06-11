namespace SchemaStudioWebViewer.Models
{
    public static class SQLViewMock
    {
        public static readonly string SQLView = @"

        CREATE VIEW[dbo].[SchemaStudio_Excede_SVSLS]
        AS
SELECT

    -- SERVICE HEADER
    S.SlsId, /*@BusinessName: Service Sales ID @BusinessDescription: Workorder Document Number Including :01 Iteration */
    S.SlsTypId, /*@BusinessName: Sales Type ID  @BusinessDescription: Sales Type code for this workorder*/
    svslstyp.[Des] AS[Sales Type Description],
    svslstyp.[SalesTypeGroup],
    S.UntId, /*@BusinessName: Unit Id @BusinessDescription: Unit ID of unit under service*/
    S.Vin,  /*@BusinessName: VIN @BusinessDescription: VIN Number of unit under service*/
    S.FleetUntId, /*@BusinessName: Fleet Unit Id @BusinessDescription: Customer Fleet Unit ID. Used by L/R as customer facing ID for the unit.*/
    S.CusId,/*@BusinessName: Customer ID @BusinessDescription: Customer ID for 'Ship To' Customer */
    S.CusName /*@BusinessName: Customer Name  @BusinessDescription: Customer Name for 'Ship To' Customer */,
    S.EmpId /*@BusinessName: Employee ID  @BusinessDescription: Employee ID of the person who created the ticket*/,
    S.EmpIdSpn/*@BusinessName: Employee Id Sales Person @BusinessDescription: Employee ID of the Salesperson for this workorder*/,
    S.EmpIdRev/*@BusinessName: Employee Id who reviewd the workorder @BusinessDescription: Employee ID of the Reviwer for this workorder may may be null until RO is posted*/,
    S.EmpIdWriter,/*@BusinessName: Employee Id of the service writer for the workorder @BusinessDescription: Employee ID of the Service writer for this workorder may may be null until RO is posted*/
    S.Priority,
    S.Wait,
    S.Comeback,
    S.CountJobs,
    S.CountJobsIncomplete,
    S.CountJobsOther,
    S.CountJobsIncompleteOther,
    S.AmtSubtotal,
    S.AmtParts,
    S.AmtLabor,
    S.AmtSublet,
    S.AmtMisc,
    S.AmtSupplies,
    S.AmtDiagnostic,
    S.AmtTax1,
    S.AmtCostSubtotal,
    S.AmtCostParts,
    S.AmtCostLabor,
    S.AmtCostSublet,
    S.AmtCostMisc,
    S.AmtSubtotalOther,
    S.AmtPartsOther,
    S.AmtLaborOther,
    S.AmtSubletOther,
    S.AmtMiscOther,
    S.AmtCostSubtotalOther,
    S.AmtCostPartsOther,
    S.AmtCostLaborOther,
    S.AmtCostSubletOther,
    S.AmtCostMiscOther,
    S.AmtLabor + S.AmtLaborOther AS 'AmtLaborTotal' ,/*@BusinessName: Labor Subtotal Amount @BusinessDescription: Labor sold Including Other Labor */
    S.AmtParts + S.AmtPartsOther AS 'AmtPartsTotal'  ,
    S.AmtMisc + S.AmtMiscOther AS 'AmtMiscTotal',
    S.AmtSublet + S.AmtSubletOther AS 'AmtSubletTotal',
    S.AmtCostLabor + S.AmtCostLaborOther AS 'AmtCostLaborTotal' ,
    S.AmtCostParts + S.AmtCostPartsOther AS 'AmtCostPartsTotal' ,
    S.AmtCostMisc + S.AmtCostMiscOther AS  'AmtCostMiscTotal' ,
    S.AmtCostSublet + S.AmtCostSubletOther AS  'AmtCostSubletTotal',
    S.AmtParts - S.AmtCostParts AS 'AmtGrossProfitPart',
    S.AmtLabor - S.AmtCostLabor AS 'AmtGrossProfitLabor',
    S.AmtSublet - S.AmtCostSublet AS 'AmtGrossProfitSublet',
    S.AmtMisc - S.AmtCostMisc AS 'AmtGrossProfitMisc',
    S.AmtLabor - S.AmtCostLabor + S.AmtSublet - S.AmtCostSublet + S.AmtMisc - S.AmtCostMisc AS 'AmtGrossProfitNOPARTS' ,
    S.AmtParts - S.AmtCostParts + S.AmtLabor - S.AmtCostLabor + S.AmtSublet
                   - S.AmtCostSublet + S.AmtMisc - S.AmtCostMisc AS 'AmtGrossProfitAll',
    S.HrsFlat,
    S.HrsBill,
    S.HrsCost,
    S.HrsActual,
    S.HrsFlatOther,
    S.HrsBillOther,
    S.HrsCostOther,
    S.HrsActualOther,
    S.HrsFlat + S.HrsFlatOther AS TotalHoursFlat,
    S.HrsBill + S.HrsBillOther AS TotalHoursBill,
    S.HrsCost + S.HrsCostOther AS TotalHoursCost,
    S.HrsActual + S.HrsActualOther AS TotalHoursActual,
    S.BillCusId,
    S.BillCusName,
    S.TrmId,
    S.JeId,
    S.Posted,
    S.DateCreate,
    S.DateInvoice,
    S.InvoiceSlsId,
    S.Estimate,
    S.AmtEstimate,
    S.AmtEstimateRevised,
    S.Status,
    S.BrnId
FROM[ExcedeUS2025YE].dbo.SVSLS AS S WITH(NOLOCK)
    JOIN VVGBI_Integrations.[dbo].[SchemaStudio_Excede_SVSLSTYP] svslstyp ON s.slstypid = svslstyp.slstypid
--LEFT OUTER JOIN[ExcedeUS2025YE].dbo.SVSLSOPS AS O WITH (NOLOCK) ON S.SlsId = O.SlsId
--LEFT OUTER JOIN[ExcedeUS2025YE].dbo.SVSLSITM AS I WITH (NOLOCK) ON O.SlsId = I.SlsId AND O.OpsId = I.OpsId
--JOIN[ExcedeUS2025YE].dbo.COEMPLBR AS TP WITH (NOLOCK) ON I.ItmId = TP.ParentItmId
--JOIN[ExcedeUS2025YE].dbo.SVSLSTYP AS ST WITH (NOLOCK) ON ST.Slstypid = S.slstypid
--left outer JOIN[ExcedeUS2025YE].dbo.COEMP AS EL WITH (NOLOCK) ON EL.EMPID = I.EmpidLbr
--JOIN[ExcedeUS2025YE].dbo.COEMP AS EW WITH (nolock) ON EW.empid = s.Empidwriter
--INNER JOIN [ExcedeUS2025YE].dbo.COLOOKUP L1 ON I.ItmTyp = L1.Id;
 ";

    }
}
