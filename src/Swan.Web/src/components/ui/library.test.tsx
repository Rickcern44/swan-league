import { render, screen } from "@testing-library/react";
import { expect, test } from "vitest";
import { Alert, AlertDescription, AlertTitle } from "./alert";
import { Button } from "./button";
import { Card, CardContent, CardHeader, CardTitle } from "./card";
import { Input } from "./input";
import { Label } from "./label";

test("the Swan component library composes accessible shadcn primitives", () => {
  render(
    <Card>
      <CardHeader>
        <CardTitle>League details</CardTitle>
      </CardHeader>
      <CardContent>
        <Label htmlFor="season-name">Season name</Label>
        <Input id="season-name" />
        <Button type="button">Save draft</Button>
        <Alert>
          <AlertTitle>Saved</AlertTitle>
          <AlertDescription>Your changes are ready.</AlertDescription>
        </Alert>
      </CardContent>
    </Card>,
  );

  expect(screen.getByText("League details")).toBeVisible();
  expect(screen.getByLabelText("Season name")).toBeVisible();
  expect(screen.getByRole("button", { name: "Save draft" })).toBeEnabled();
  expect(screen.getByText("Your changes are ready.")).toBeVisible();
});
