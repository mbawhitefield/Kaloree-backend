namespace Kaloree.Api.Domain;

public static class Nutrition
{
    public static int Age(DateOnly birth, DateOnly today) => today.Year - birth.Year - (birth > today.AddYears(-(today.Year - birth.Year)) ? 1 : 0);

    public static decimal Bmr(decimal kg, decimal cm, int age, string sex) =>
        Math.Round(10 * kg + 6.25m * cm - 5 * age + (sex == "male" ? 5 : -161));

    public static decimal Tdee(decimal bmr, decimal activity) => Math.Round(bmr * activity);

    public static decimal Scale(decimal per100g, decimal grams) => Math.Round(per100g * grams / 100, 3);
}
